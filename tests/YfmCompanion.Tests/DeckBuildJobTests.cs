using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

public sealed class DeckBuildJobTests
{
    private static YfmCompanion.Data.FusionCatalog Catalog() => TestCatalogFactory.Create(
        Enumerable.Range(1, 14).Select(id => TestCatalogFactory.Card(id, $"Card {id}", id * 50))
            .Append(TestCatalogFactory.Card(100, "Fusion", 2800)), [TestCatalogFactory.Pair(1, 2, 100)]);
    private static DeckBuildRequest Request(DeckSearchMode mode = DeckSearchMode.Quick) => new(
        Enumerable.Range(1, 14).Select(id => new OwnedCardQuantity(id, 3)).ToArray(), new(SampleHands: 24), mode);

    [Fact]
    public async Task StopKeepsEarlyLegalDeckAndVerifyDoesNotClaimOptimality()
    {
        var job = new DeckBuildJob(Catalog(), Request());
        var stopped = await job.RunAsync(new Callback<DeckBuildProgress>(p => { if (p.Best is not null) job.StopAndKeepBest(); }));
        Assert.Equal(DeckBuildState.Cancelled, stopped.State);
        Assert.NotNull(stopped.Best);
        Assert.Equal(40, stopped.Best.Report.TotalCards);
        Assert.All(stopped.Best.Report.Deck, entry => Assert.False(string.IsNullOrWhiteSpace(entry.ContributionReason)));
        Assert.NotEmpty(stopped.Best.Report.ImportantTargets);
        Assert.False(stopped.Best.Report.ExactAnalysis.IsExact);
        Assert.False(stopped.ProvenOptimal);
        var verified = await job.VerifyBestAsync();
        Assert.Equal(DeckBuildState.Completed, verified.State);
        Assert.True(verified.Best!.Report.ExactAnalysis.IsExact);
        Assert.Equal(658008, verified.Best.Report.ExactAnalysis.TotalHands);
        Assert.False(verified.ProvenOptimal);
    }

    [Fact]
    public async Task PauseResumeFreezesInventoryAndRetainsVerifiedIncumbentAcrossModes()
    {
        var inventory = Enumerable.Range(1, 14).Select(id => new OwnedCardQuantity(id, 3)).ToArray();
        var request = Request() with { OwnedCards = inventory };
        var catalog = Catalog();
        var job = new DeckBuildJob(catalog, request);
        inventory[0] = new(1, 0);
        var paused = await job.RunAsync(new Callback<DeckBuildProgress>(p => { if (p.Best is not null) job.Pause(); }));
        Assert.Equal(DeckBuildState.Paused, paused.State);
        var stopped = await job.RunAsync(new Callback<DeckBuildProgress>(_ => job.StopAndKeepBest()));
        Assert.NotNull(stopped.Best);
        var verified = await job.VerifyBestAsync();
        var longer = new DeckBuildJob(catalog, Request(DeckSearchMode.Thorough));
        longer.AdoptVerifiedIncumbent(verified);
        var next = await longer.RunAsync(new Callback<DeckBuildProgress>(_ => longer.StopAndKeepBest()));
        Assert.NotSame(verified.Best!.Report, next.Best!.Report);
        Assert.Equal(verified.Best.Report.Deck, next.Best.Report.Deck);
        Assert.Throws<ArgumentException>(() => new DeckBuildJob(catalog, Request() with { StarChips = 1 }).AdoptVerifiedIncumbent(verified));
    }

    [Fact]
    public async Task QuickConsumesItsBudgetAndReturnsClearlySampledResult()
    {
        var job = new DeckBuildJob(Catalog(), Request());
        var updates = new List<DeckBuildProgress>();
        var result = await job.RunAsync(new Callback<DeckBuildProgress>(updates.Add));
        Assert.Equal(DeckBuildState.Completed, result.State);
        Assert.False(result.ProvenOptimal);
        Assert.False(result.Best!.Report.ExactAnalysis.IsExact);
        Assert.Equal(24, result.Best.Report.ExactAnalysis.SampleCount);
        Assert.True(updates.Count >= 5);
        // Cancellation timers are millisecond-resolution and may fire just before the exact boundary.
        Assert.InRange(updates[^1].SearchTimeConsumed.TotalSeconds, 4.9, 7);
        Assert.Equal(TimeSpan.FromSeconds(5), DeckBuildJob.BudgetFor(DeckSearchMode.Quick));
        Assert.Equal(TimeSpan.FromSeconds(60), DeckBuildJob.BudgetFor(DeckSearchMode.Balanced));
        Assert.Equal(TimeSpan.FromSeconds(900), DeckBuildJob.BudgetFor(DeckSearchMode.Thorough));
        Assert.Null(DeckBuildJob.BudgetFor(DeckSearchMode.ProveOptimal));
    }

    [Fact]
    public async Task ProofPauseKeepsCompatibleVerifiedIncumbent()
    {
        var catalog = Catalog();
        var quick = new DeckBuildJob(catalog, Request());
        await quick.RunAsync(new Callback<DeckBuildProgress>(p => { if (p.Best is not null) quick.StopAndKeepBest(); }));
        var verified = await quick.VerifyBestAsync();
        var path = Path.Combine(Path.GetTempPath(), "yfm-proof-incumbent", Guid.NewGuid().ToString("N"), "proof.json");
        try
        {
            var proof = new DeckBuildJob(catalog, Request(DeckSearchMode.ProveOptimal), path);
            proof.AdoptVerifiedIncumbent(verified);
            var paused = await proof.RunAsync(new Callback<DeckBuildProgress>(_ => proof.Pause()));
            Assert.Equal(DeckBuildState.Paused, paused.State);
            Assert.NotNull(paused.Best);
            Assert.True(paused.Best.Report.ExactAnalysis.IsExact);
            Assert.Equal(verified.Best!.Report.Deck, paused.Best.Report.Deck);
            Assert.False(paused.ProvenOptimal);
            var freshJob = new DeckBuildJob(catalog, Request(DeckSearchMode.ProveOptimal), path);
            var recovered = await freshJob.RunAsync(new Callback<DeckBuildProgress>(_ => freshJob.Pause()));
            Assert.Equal(verified.Best.Report.Deck, recovered.Best!.Report.Deck);
            Assert.False(recovered.ProvenOptimal);
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public async Task InfeasibleCollectionFailsWithoutPublishingAnIllegalDeck()
    {
        var job = new DeckBuildJob(Catalog(), Request() with { OwnedCards = [new(1, 3)] });
        await Assert.ThrowsAsync<InvalidOperationException>(() => job.RunAsync());
        Assert.Equal(DeckBuildState.Failed, job.State);
    }

    private sealed class Callback<T>(Action<T> callback) : IProgress<T> { public void Report(T value) => callback(value); }
}
