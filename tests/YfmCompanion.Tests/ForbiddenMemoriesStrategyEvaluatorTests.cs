using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class ForbiddenMemoriesStrategyEvaluatorTests(DatabaseFixture fixture)
{
    [Fact]
    public void CanonicalFieldRulesApplyTypeBonusesAndPenalties()
    {
        Assert.Equal(500, ForbiddenMemoriesStrategyEvaluator.GetFieldModifier(332, "Dragon"));
        Assert.Equal(500, ForbiddenMemoriesStrategyEvaluator.GetFieldModifier(332, "Thunder"));
        Assert.Equal(500, ForbiddenMemoriesStrategyEvaluator.GetFieldModifier(334, "Aqua"));
        Assert.Equal(-500, ForbiddenMemoriesStrategyEvaluator.GetFieldModifier(334, "Pyro"));
        Assert.Equal(-500, ForbiddenMemoriesStrategyEvaluator.GetFieldModifier(335, "Fairy"));
        Assert.Equal(0, ForbiddenMemoriesStrategyEvaluator.GetFieldModifier(330, "Dragon"));
    }

    [Fact]
    public void BroadRemovalOutranksNoEffectAndTinyThresholdTraps()
    {
        var evaluator = new ForbiddenMemoriesStrategyEvaluator(fixture.Catalog);
        var options = new DeckOptimizationOptions();
        var widespread = evaluator.Assess(fixture.Catalog.GetCard(686), options);
        var fakeTrap = evaluator.Assess(fixture.Catalog.GetCard(690), options);
        var houseOfAdhesiveTape = evaluator.Assess(fixture.Catalog.GetCard(681), options);

        Assert.Equal(CardViabilityTier.Essential, widespread.Tier);
        Assert.Equal(CardViabilityTier.NonViable, fakeTrap.Tier);
        Assert.Equal(CardViabilityTier.NonViable, houseOfAdhesiveTape.Tier);
        Assert.True(widespread.StrategicScore > fakeTrap.StrategicScore);
    }

    [Fact]
    public void RitualsAreExcludedNormallyButAvailableForExplicitExperiments()
    {
        var evaluator = new ForbiddenMemoriesStrategyEvaluator(fixture.Catalog);
        var ritual = fixture.Catalog.Cards.First(card => card.PrimaryType.Equals("Ritual", StringComparison.OrdinalIgnoreCase));

        var normal = evaluator.Assess(ritual, new DeckOptimizationOptions());
        var experiment = evaluator.Assess(ritual, new DeckOptimizationOptions(Profile: DeckStrategyProfile.RitualExperiment));

        Assert.Equal(CardViabilityTier.NonViable, normal.Tier);
        Assert.Equal(CardViabilityTier.Situational, experiment.Tier);
        Assert.True(experiment.StrategicScore > normal.StrategicScore);
    }

    [Fact]
    public void RequestedMatchupRaisesOnlyRelevantTypedRemoval()
    {
        var evaluator = new ForbiddenMemoriesStrategyEvaluator(fixture.Catalog);
        var options = new DeckOptimizationOptions(OpponentMonsterTypes: ["Dragon"]);

        var dragonJar = evaluator.Assess(fixture.Catalog.GetCard(329), options);
        var warriorElimination = evaluator.Assess(fixture.Catalog.GetCard(653), options);

        Assert.Equal(CardViabilityTier.Strong, dragonJar.Tier);
        Assert.Equal(CardViabilityTier.Situational, warriorElimination.Tier);
        Assert.True(dragonJar.StrategicScore > warriorElimination.StrategicScore);
    }
}
