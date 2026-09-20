using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class CampaignOptimizationContextBuilderTests(DatabaseFixture fixture)
{
    [Fact]
    public void GeneralSafetyUsesAllThirtyThreeNonGauntletOpponents()
    {
        var research = CampaignResearchData.LoadBundled(AppContext.BaseDirectory);

        var context = new CampaignOptimizationContextBuilder(fixture.Catalog, research)
            .Build(CampaignOpponentScope.GeneralSafety);

        Assert.Equal(33, context.Safety.OpponentIds.Count);
        Assert.Equal(33, context.OpponentThreatReports.Count);
        Assert.DoesNotContain(context.Safety.OpponentIds, research.Policy.FinalGauntletDuelistIds.Contains);
        Assert.All(context.OpponentThreatReports, report =>
            Assert.Contains("not a duel win probability", report.Methodology, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("conservatively", context.Safety.Methodology, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SpecificOpponentAndFinalGauntletRemainSeparateScopes()
    {
        var research = CampaignResearchData.LoadBundled(AppContext.BaseDirectory);
        var builder = new CampaignOptimizationContextBuilder(fixture.Catalog, research);

        var specific = builder.Build(CampaignOpponentScope.SpecificOpponent, specificOpponentId: 36);
        var gauntlet = builder.Build(CampaignOpponentScope.FinalGauntlet);

        Assert.Equal([36], specific.Safety.OpponentIds);
        Assert.Contains("Seto 3rd", specific.Safety.Label, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(6, gauntlet.Safety.OpponentIds.Count);
        Assert.Equal(research.Policy.FinalGauntletDuelistIds.Order(), gauntlet.Safety.OpponentIds);
        Assert.Throws<ArgumentException>(() => builder.Build(CampaignOpponentScope.SpecificOpponent));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.Build(CampaignOpponentScope.SpecificOpponent, specificOpponentId: 99));
    }

    [Fact]
    public void CampaignOptimizerCarriesGeneralAndGauntletSafetyIntoTheFinalistReport()
    {
        var research = CampaignResearchData.LoadBundled(AppContext.BaseDirectory);
        var owned = Enumerable.Range(1, 14).Select(cardId => new OwnedCardQuantity(cardId, 3));

        var result = new CampaignDeckOptimizer(fixture.Catalog, research).Optimize(
            owned,
            starChips: 0,
            useStarChips: false,
            CampaignOpponentScope.GeneralSafety,
            options: new DeckOptimizationOptions(
                SampleHands: 2,
                ExactFinalists: 1,
                IncludeGlitches: false,
                Profile: DeckStrategyProfile.ControlAndSafety));

        Assert.Equal(40, result.DeckPlan.ResultingDeck.TotalCards);
        Assert.NotNull(result.DeckPlan.ResultingDeck.SafetyAssessment);
        Assert.NotNull(result.DeckPlan.ResultingDeck.SecondarySafetyAssessment);
        Assert.Contains("not a win probability", result.DeckPlan.ResultingDeck.SafetyAssessment!.Methodology, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Best-found", result.Methodology, StringComparison.OrdinalIgnoreCase);
    }
}
