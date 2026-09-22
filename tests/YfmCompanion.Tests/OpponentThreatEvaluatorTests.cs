using YfmCompanion.Data;
using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

public sealed class OpponentThreatEvaluatorTests
{
    [Fact]
    public void FindsConcreteReachableFusionsAndNeverLabelsWeightsAsProbabilities()
    {
        var cards = new[]
        {
            Monster(1, "Dragon", 1000, "Sun"),
            Monster(2, "Thunder", 900, "Mars"),
            Monster(3, "Beast", 1300, "Moon"),
            Monster(4, "Twin", 2800, "Pluto")
        };
        var catalog = TestCatalogFactory.Create(cards, [TestCatalogFactory.Pair(1, 2, 4)]);

        var report = new OpponentThreatEvaluator(catalog).Evaluate(
            [new(1, 100), new(2, 50), new(3, 200)]);

        Assert.Equal(3, report.StrongestBaseMonster.Id);
        var threat = Assert.Single(report.FusionThreats);
        Assert.Equal(4, threat.Result.Id);
        Assert.Equal(5000, threat.OpportunityWeight);
        Assert.Equal("Pluto", threat.FirstGuardianStar);
        Assert.Contains("not a duel win probability", report.Methodology, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelfFusionAndGlitchFilteringFollowTheConcreteCatalog()
    {
        var cards = new[]
        {
            Monster(1, "Material", 1000, "Sun"),
            Monster(2, "Intended", 1800, "Moon"),
            Monster(3, "Glitch", 3000, "Mars")
        };
        var catalog = TestCatalogFactory.Create(
            cards,
            [TestCatalogFactory.Pair(1, 1, 2), TestCatalogFactory.Pair(1, 2, 3, glitch: true)]);
        var evaluator = new OpponentThreatEvaluator(catalog);

        var intendedOnly = evaluator.Evaluate([new(1, 10), new(2, 5)]);
        Assert.Equal([2], intendedOnly.FusionThreats.Select(item => item.Result.Id));
        Assert.Equal(100, intendedOnly.FusionThreats[0].OpportunityWeight);

        var withGlitches = evaluator.Evaluate([new(1, 10), new(2, 5)], includeGlitches: true);
        Assert.Equal([3, 2], withGlitches.FusionThreats.Select(item => item.Result.Id));
    }

    [Fact]
    public void InvalidPoolsFailClosed()
    {
        var catalog = TestCatalogFactory.Create([Monster(1, "Only", 1000, "Sun")], []);
        var evaluator = new OpponentThreatEvaluator(catalog);

        Assert.Throws<ArgumentException>(() => evaluator.Evaluate([]));
        Assert.Throws<ArgumentException>(() => evaluator.Evaluate([new(1, 1), new(1, 2)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate([new(1, 0)]));
        Assert.Throws<KeyNotFoundException>(() => evaluator.Evaluate([new(99, 1)]));
    }

    [Fact]
    public void FindsOrderedChainsUsingUpToAllFiveHandMaterials()
    {
        var cards = Enumerable.Range(1, 9)
            .Select(id => Monster(id, $"Card {id}", id * 100, "Sun"))
            .ToArray();
        var catalog = TestCatalogFactory.Create(cards, [
            TestCatalogFactory.Pair(1, 2, 6),
            TestCatalogFactory.Pair(3, 6, 7),
            TestCatalogFactory.Pair(4, 7, 8),
            TestCatalogFactory.Pair(5, 8, 9)
        ]);

        var report = new OpponentThreatEvaluator(catalog).Evaluate([
            new(1, 10), new(2, 10), new(3, 10), new(4, 10), new(5, 10)
        ]);

        Assert.Contains(report.FusionThreats, threat => threat.Result.Id == 9);
        Assert.Contains("five-material", report.Methodology, StringComparison.OrdinalIgnoreCase);
    }

    private static Card Monster(int id, string name, int attack, string guardianStar) =>
        new(id, name, null, guardianStar, null, 4, "Test", null, attack, attack, null, 100, true, true, true);
}
