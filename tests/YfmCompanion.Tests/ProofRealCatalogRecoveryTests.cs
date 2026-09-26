using System.Diagnostics;
using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class ProofRealCatalogRecoveryTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task InterruptedLeafIsReplayedRatherThanCountedAsComplete()
    {
        var cards = fixture.Catalog.Cards.Where(c => c.Attack > 0).OrderBy(c => c.Id).Take(40).Select(c => c.Id).ToArray();
        var request = new DeckBuildRequest([.. cards.Select(id => new OwnedCardQuantity(id, 1))], new(), DeckSearchMode.ProveOptimal);
        var path = Path.Combine(Path.GetTempPath(), "yfm-proof-partial", Guid.NewGuid().ToString("N"), "checkpoint.json");
        using var cancel = new CancellationTokenSource();
        var delay = new Stopwatch();
        try
        {
            var paused = await new DeckProofSearch(fixture.Catalog).RunAsync(request, path,
                new Callback<DeckProofProgress>(p =>
                {
                    if (p.State == DeckBuildState.Verifying && p.CompletedHands > 0 && p.CompletedHands < p.TotalHands)
                    { delay.Start(); cancel.Cancel(); }
                }), cancel.Token);
            Assert.True(delay.IsRunning, "Fixture must interrupt a partially analyzed deck, not preparation or a completed leaf.");
            Assert.Equal(DeckBuildState.Paused, paused.State);
            Assert.Equal(0, (int)paused.ResolvedSpace);
            Assert.Equal(0, (int)paused.LegalDecksEvaluated);
            Assert.Null(paused.Best);
            Assert.True(delay.Elapsed < TimeSpan.FromSeconds(1));
            var resumed = await new DeckProofSearch(fixture.Catalog).RunAsync(request, path);
            var independent = new DeckAnalyzer(fixture.Catalog, 0).Analyze(cards);
            Assert.True(resumed.ProvenOptimal);
            Assert.Equal(1, (int)resumed.LegalDecksEvaluated);
            Assert.Equal(independent.TotalBestFusionAttack, resumed.Best!.ExactAnalysis.TotalBestFusionAttack);
            Assert.Equal(658008, resumed.Best.ExactAnalysis.TotalHands);
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }
    private sealed class Callback<T>(Action<T> callback) : IProgress<T> { public void Report(T value) => callback(value); }
}
