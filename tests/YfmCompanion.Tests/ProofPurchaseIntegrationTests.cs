using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

public sealed class ProofPurchaseIntegrationTests
{
    [Theory]
    [InlineData(10, false, 38, 2, 10)]
    [InlineData(5, false, 39, 1, 5)]
    [InlineData(10, true, 39, 1, 5)]
    public async Task ProofSelectsIndependentBestAffordableVector(uint budget, bool redeemed,
        int firstCopies, int secondCopies, long expectedSpend)
    {
        var catalog = TestCatalogFactory.Create(
            [TestCatalogFactory.Card(1, "One") with { Password = "12345678", StarchipCost = 5 },
             TestCatalogFactory.Card(2, "Two") with { Password = "23456789", StarchipCost = 10 },
             TestCatalogFactory.Card(100, "Result", 2800)], [TestCatalogFactory.Pair(1, 2, 100)]);
        var options = new DeckOptimizationOptions(CopyLimit: 40,
            AlreadyRedeemedCardNames: redeemed ? new HashSet<string> { "TWO" } : null);
        var request = new DeckBuildRequest([new(1, 38), new(2, 1)], options,
            DeckSearchMode.ProveOptimal, true, budget);
        var path = Path.Combine(Path.GetTempPath(), "yfm-proof-purchase", Guid.NewGuid().ToString("N"), "proof.json");
        try
        {
            var result = await new DeckProofSearch(catalog).RunAsync(request, path);
            Assert.True(result.ProvenOptimal);
            Assert.Equal(expectedSpend, result.RequiredStarChips);
            Assert.Equal(firstCopies, result.Best!.Deck.Single(e => e.Card.Id == 1).Copies);
            Assert.Equal(secondCopies, result.Best.Deck.Single(e => e.Card.Id == 2).Copies);
            // The only recipe requires both materials: inclusion-exclusion is an independent oracle.
            var expectedHands = 658008 - DeckAnalyzer.Choose(40 - firstCopies, 5)
                - DeckAnalyzer.Choose(40 - secondCopies, 5);
            Assert.Equal(expectedHands, result.Best.ExactAnalysis.HandsWithAnyFusion);
            Assert.Equal(expectedHands * 2800, result.Best.ExactAnalysis.TotalBestFusionAttack);
            var resumed = await new DeckProofSearch(catalog).RunAsync(request, path);
            Assert.Equal(result.LegalDecksEvaluated, resumed.LegalDecksEvaluated);
            Assert.Equal(expectedSpend, resumed.RequiredStarChips);
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public async Task ProofWithNoAffordableCompletionDoesNotClaimOptimalDeck()
    {
        var catalog = TestCatalogFactory.Create(
            [TestCatalogFactory.Card(1, "One") with { Password = "12345678", StarchipCost = 5 }], []);
        var path = Path.Combine(Path.GetTempPath(), "yfm-proof-purchase", Guid.NewGuid().ToString("N"), "proof.json");
        try
        {
            var result = await new DeckProofSearch(catalog).RunAsync(
                new([new(1, 39)], new(CopyLimit: 40), DeckSearchMode.ProveOptimal, true, 4), path);
            Assert.Equal(DeckBuildState.Completed, result.State);
            Assert.False(result.ProvenOptimal);
            Assert.Null(result.Best);
            Assert.Equal(result.TotalSpace, result.ResolvedSpace);
            Assert.Equal(0, (int)result.LegalDecksEvaluated);
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }
}
