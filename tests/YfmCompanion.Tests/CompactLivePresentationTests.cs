using YfmCompanion.Desktop;
using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

public sealed class CompactLivePresentationTests
{
    [Fact]
    public void CreateRow_ShowsOnlyResultEffectiveAttackAndHandSlotOrder()
    {
        var result = TestCatalogFactory.Card(613, "Twin-headed Thunder Dragon", 2_800, 2_100);
        var recommendation = Recommendation(result, [1, 3, 5], attackBonus: 500);

        var row = CompactLivePresentation.CreateRow(recommendation);

        Assert.Equal("Twin-headed Thunder Dragon", row.Result);
        Assert.Equal(3_300, row.Attack);
        Assert.Equal("1+3+5", row.Route);
        Assert.Equal("?", row.GuardianStar1);
        Assert.Equal("—", row.GuardianOutcomes1);
    }

    [Fact]
    public void CreateRowKeepsGuardianLinesInsideTheExistingRoutePresentation()
    {
        var result = TestCatalogFactory.Card(10, "Result", 1_500);
        var recommendation = Recommendation(result, [2, 4]);
        var guardian = new GuardianLiveAdvice("☿ > ☉ > ☾", "♆ > ♂ > ♃", "F1✓ F2?", "F1× F2?");

        var row = CompactLivePresentation.CreateRow(recommendation, guardian);

        Assert.Equal("2+4", row.Route);
        Assert.Equal("☿ > ☉ > ☾", row.GuardianStar1);
        Assert.Equal("♆ > ♂ > ♃", row.GuardianStar2);
        Assert.Equal("F1✓ F2?", row.GuardianOutcomes1);
        Assert.Equal("F1× F2?", row.GuardianOutcomes2);
    }

    [Theory]
    [InlineData(FieldZone.Monster, 1, "F(1)+2+4")]
    [InlineData(FieldZone.Monster, 5, "F(5)+2+4")]
    [InlineData(FieldZone.SpellTrap, 1, "F(6)+2+4")]
    [InlineData(FieldZone.SpellTrap, 5, "F(10)+2+4")]
    public void FormatRoute_MapsFieldZonesToRequestedOneThroughTenNotation(
        FieldZone zone,
        int slot,
        string expected)
    {
        var result = TestCatalogFactory.Card(10, "Result", 1_500);
        var recommendation = Recommendation(result, [2, 4], new FieldCard(zone, slot, 99));

        Assert.Equal(expected, CompactLivePresentation.FormatRoute(recommendation));
    }

    private static TacticalRecommendation Recommendation(
        YfmCompanion.Data.Card result,
        IReadOnlyList<int> handSlots,
        FieldCard? fieldTarget = null,
        int attackBonus = 0) =>
        new(result, [], handSlots, fieldTarget, false, attackBonus);
}
