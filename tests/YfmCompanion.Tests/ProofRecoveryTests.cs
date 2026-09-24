using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using YfmCompanion.Data;
using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

public sealed class ProofRecoveryTests
{
    private static FusionCatalog Catalog(int attack = 2800) => TestCatalogFactory.Create(
        [TestCatalogFactory.Card(1, "One"), TestCatalogFactory.Card(2, "Two"), TestCatalogFactory.Card(3, "Three"), TestCatalogFactory.Card(100, "Result", attack)],
        [TestCatalogFactory.Pair(1, 2, 100)]);
    private static DeckBuildRequest Request() => new([new(1, 39), new(2, 1), new(3, 1)], new(CopyLimit: 40), DeckSearchMode.ProveOptimal);
    private static string NewPath() => Path.Combine(Path.GetTempPath(), "yfm-proof-recovery", Guid.NewGuid().ToString("N"), "checkpoint.json");

    [Fact]
    public async Task ImmediatePreparationPauseIsDurableIncludingThroughJobWrapper()
    {
        var path = NewPath();
        try
        {
            var catalog = Catalog();
            var job = new DeckBuildJob(catalog, Request(), path);
            var paused = await job.RunAsync(new Callback<DeckBuildProgress>(_ => job.Pause()));
            Assert.Equal(DeckBuildState.Paused, paused.State);
            Assert.True(File.Exists(path));
            var completed = await job.RunAsync();
            Assert.True(completed.ProvenOptimal);
            Assert.Equal(3, (int)completed.ProofLegalDecksEvaluated!.Value);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public async Task ChangedCatalogAndObjectiveAreRejectedWithoutOverwritingCheckpoint()
    {
        var path = NewPath();
        try
        {
            await new DeckProofSearch(Catalog()).RunAsync(Request(), path, cancellationToken: new(true));
            var original = File.ReadAllBytes(path);
            await Assert.ThrowsAsync<InvalidDataException>(() => new DeckProofSearch(Catalog(3200)).RunAsync(Request(), path));
            Assert.Equal(original, File.ReadAllBytes(path));
            var envelope = JsonNode.Parse(Encoding.UTF8.GetString(original))!;
            var payload = JsonNode.Parse(envelope["Payload"]!.GetValue<string>())!;
            payload["ObjectiveVersion"] = "different-objective";
            var text = payload.ToJsonString();
            envelope["Payload"] = text;
            envelope["Checksum"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            File.WriteAllText(path, envelope.ToJsonString());
            await Assert.ThrowsAsync<InvalidDataException>(() => new DeckProofSearch(Catalog()).RunAsync(Request(), path));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public async Task ExclusiveLeasePreventsTwoJobsOverwritingTheSameSearch()
    {
        var path = NewPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            using (var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
                await Assert.ThrowsAsync<IOException>(() => new DeckProofSearch(Catalog()).RunAsync(Request(), path));
            Assert.False(File.Exists(path));
            Assert.True((await new DeckProofSearch(Catalog()).RunAsync(Request(), path)).ProvenOptimal);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public async Task PreviewCountsSpaceAndUsesMeasuredEstimateWithoutClaimingProof()
    {
        var preview = await DeckProofPreflight.PreviewAsync(Catalog(), Request());
        Assert.Equal(3, (int)preview.CapacityVectors);
        Assert.True(preview.HasFeasibleDeck);
        Assert.True(preview.MeasuredDeckMilliseconds >= 0);
        Assert.True(preview.UnprunedWorkHighMilliseconds >= preview.UnprunedWorkLowMilliseconds);
        var noMeasurement = await DeckProofPreflight.PreviewAsync(Catalog(), Request(), false);
        Assert.Null(noMeasurement.MeasuredDeckMilliseconds);
        Assert.Null(noMeasurement.UnprunedWorkHighMilliseconds);
        var impossible = await DeckProofPreflight.PreviewAsync(Catalog(), Request() with { OwnedCards = [new(1, 3)] });
        Assert.False(impossible.HasFeasibleDeck);
        Assert.Null(impossible.MeasuredDeckMilliseconds);
    }

    [Fact]
    public async Task PublishedDeckAndRoutesCannotMutateTheInternalWinner()
    {
        var path = NewPath();
        try
        {
            var result = await new DeckProofSearch(Catalog()).RunAsync(Request(), path);
            Assert.Throws<NotSupportedException>(() => ((IList<OptimizedDeckEntry>)result.Best!.Deck).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<Card>)result.Best!.ExactAnalysis.FusionResults[0].RepresentativeRoute.Materials).Clear());
            var resumed = await new DeckProofSearch(Catalog()).RunAsync(Request(), path);
            Assert.Throws<NotSupportedException>(() => ((IList<OptimizedDeckEntry>)resumed.Best!.Deck).Clear());
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    private sealed class Callback<T>(Action<T> callback) : IProgress<T> { public void Report(T value) => callback(value); }
}
