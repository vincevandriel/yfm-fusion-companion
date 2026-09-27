using System.Text.Json;
using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class CpuParallelismTests(DatabaseFixture fixture)
{
    [Fact]
    public void DescribingCompletedWorkAfterPauseDoesNotReuseCancelledAssessmentToken()
    {
        var deck = fixture.Catalog.Cards.Where(c => c.Id is not (>= 17 and <= 21)).OrderBy(c => c.Id)
            .Take(14).SelectMany(c => Enumerable.Repeat(c.Id, 3)).Take(40).ToArray();
        var owned = deck.GroupBy(id => id).Select(g => new OwnedCardQuantity(g.Key, g.Count())).ToArray();
        var optimizer = new OwnedDeckOptimizer(fixture.Catalog);
        var options = new DeckOptimizationOptions(SampleHands: 24);
        using var pausedToken = new CancellationTokenSource();
        var completed = optimizer.EvaluateDeck(deck, owned, options, false, cancellationToken: pausedToken.Token);
        pausedToken.Cancel();
        var describe = typeof(OwnedDeckOptimizer).GetMethod("DescribeCandidate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
            [typeof(DeckOptimizationReport), typeof(IEnumerable<OwnedCardQuantity>), typeof(DeckOptimizationOptions), typeof(CancellationToken)], null)!;
        var described = (DeckOptimizationReport)describe.Invoke(optimizer, [completed, owned, options, CancellationToken.None])!;
        Assert.Equal(40, described.TotalCards);
        Assert.All(described.Deck, entry => Assert.False(string.IsNullOrWhiteSpace(entry.ContributionReason)));
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    public void ExactMetricsAndRepresentativeRoutesMatchSerialWithAndWithoutCache(bool duplicateCards, int workers)
    {
        var ids = fixture.Catalog.Cards.Where(c => c.Id is not (>= 17 and <= 21)).OrderBy(c => c.Id).Select(c => c.Id);
        var deck = duplicateCards ? ids.Take(14).SelectMany(id => Enumerable.Repeat(id, 3)).Take(40).ToArray() : ids.Take(24).ToArray();
        var serial = new DeckAnalyzer(fixture.Catalog, 0).Analyze(deck, false);
        foreach (var bytes in new long[] { 0, 1024 * 1024 })
        {
            var analyzer = new DeckAnalyzer(fixture.Catalog, bytes, workers);
            var parallel = analyzer.Analyze(deck, false);
            Assert.Equal(JsonSerializer.Serialize(serial), JsonSerializer.Serialize(parallel));
            Assert.Equal(JsonSerializer.Serialize(parallel), JsonSerializer.Serialize(analyzer.Analyze(deck, false)));
            Assert.True(analyzer.CacheDiagnostics.AccountedBytes <= bytes);
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void ParallelCancellationIsRecoverableAndProgressNeverRegresses(int workers)
    {
        var deck = fixture.Catalog.Cards.OrderBy(c => c.Id).Take(40).Select(c => c.Id).ToArray();
        var analyzer = new DeckAnalyzer(fixture.Catalog, 0, workers);
        using var cancellation = new CancellationTokenSource();
        var partialObserved = false;
        Assert.ThrowsAny<OperationCanceledException>(() => analyzer.Analyze(deck, progress: new ImmediateProgress(p =>
        {
            if (p.CompletedHands <= 0 || p.CompletedHands >= p.TotalHands) return;
            Assert.True(analyzer.CacheDiagnostics.AccountedBytes <= analyzer.CacheDiagnostics.LimitBytes);
            partialObserved = true;
            cancellation.Cancel();
        }), cancellationToken: cancellation.Token));
        Assert.True(partialObserved);
        long completed = -1;
        var progress = new ImmediateProgress(p =>
        {
            Assert.InRange(p.CompletedHands, completed, p.TotalHands);
            completed = p.CompletedHands;
        });
        var report = analyzer.Analyze(deck, progress: progress);
        Assert.Equal(658008, report.TotalHands);
        Assert.Equal(report.TotalHands, completed);
        Assert.Equal(JsonSerializer.Serialize(new DeckAnalyzer(fixture.Catalog, 0).Analyze(deck)), JsonSerializer.Serialize(report));
    }

    private sealed class ImmediateProgress(Action<DeckAnalysisProgress> action) : IProgress<DeckAnalysisProgress>
    {
        public void Report(DeckAnalysisProgress value) => action(value);
    }
}
