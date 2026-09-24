using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

public sealed class DeckBuildPurchaseTests
{
    [Fact]
    public async Task EarlyJobCandidateFreezesCampaignAndRedemptionInputsAndRespectsBudget()
    {
        var catalog = TestCatalogFactory.Create(
            [TestCatalogFactory.Card(1, "One", 1200) with { Password = "12345678", StarchipCost = 5 },
             TestCatalogFactory.Card(2, "Two", 800) with { Password = "23456789", StarchipCost = 10 },
             TestCatalogFactory.Card(100, "Result", 2800)], [TestCatalogFactory.Pair(1, 2, 100)]);
        var targets = new List<DeckSafetyTarget> { new(1, "Opponent", 1, "One", 1200, [], 1, false, "Test") };
        var redeemed = new HashSet<string> { "Two" };
        var inventory = new List<OwnedCardQuantity> { new(1, 38), new(2, 1) };
        var context = new OpponentSafetyContext("Synthetic", [1], ["Test"], targets, "Modeled, not measured wins");
        var options = new DeckOptimizationOptions(CopyLimit: 40, SampleHands: 24,
            SafetyContext: context, SecondarySafetyContext: context, AlreadyRedeemedCardNames: redeemed);
        var job = new DeckBuildJob(catalog, new(inventory, options, DeckSearchMode.Quick, true, 5));
        // Caller changes after job construction must not affect ownership, scoring or exclusions.
        inventory.Clear();
        targets.Clear();
        redeemed.Clear();
        redeemed.Add("One");
        var updates = new List<DeckBuildProgress>();
        var stopped = await job.RunAsync(new Callback<DeckBuildProgress>(p =>
        {
            updates.Add(p);
            if (p.Best is not null) job.StopAndKeepBest();
        }));
        Assert.Equal(DeckBuildState.Cancelled, stopped.State);
        Assert.Equal(5, stopped.Best!.RequiredStarChips);
        Assert.Equal(39, stopped.Best.Report.Deck.Single(e => e.Card.Id == 1).Copies);
        Assert.Equal(1, stopped.Best.Report.Deck.Single(e => e.Card.Id == 2).Copies);
        Assert.Equal(1, stopped.Best.Report.SafetyAssessment!.ThreatCount);
        Assert.Equal(1, stopped.Best.Report.SecondarySafetyAssessment!.ThreatCount);
        Assert.False(stopped.Best.Report.ExactAnalysis.IsExact);
        var verified = await job.VerifyBestAsync();
        Assert.Equal(5, verified.Best!.RequiredStarChips);
        Assert.True(verified.Best.Report.ExactAnalysis.IsExact);
        Assert.Equal(658008, verified.Best.Report.ExactAnalysis.TotalHands);
        Assert.False(verified.ProvenOptimal);
        Assert.DoesNotContain(updates, p => p.Best is { RequiredStarChips: > 5 });
    }

    private sealed class Callback<T>(Action<T> callback) : IProgress<T> { public void Report(T value) => callback(value); }
}
