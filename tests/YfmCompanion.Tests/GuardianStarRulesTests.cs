using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

public sealed class GuardianStarRulesTests
{
    [Theory]
    [InlineData("Sun", "Moon")]
    [InlineData("Moon", "Venus")]
    [InlineData("Venus", "Mercury")]
    [InlineData("Mercury", "Sun")]
    [InlineData("Mars", "Jupiter")]
    [InlineData("Jupiter", "Saturn")]
    [InlineData("Saturn", "Uranus")]
    [InlineData("Uranus", "Pluto")]
    [InlineData("Pluto", "Neptune")]
    [InlineData("Neptune", "Mars")]
    public void EveryCycleEdgeAppliesTheFiveHundredPointAdvantage(string attacker, string defender)
    {
        Assert.Equal(GuardianStarOutcome.Advantage, GuardianStarRules.Resolve(attacker, defender));
        Assert.Equal(GuardianStarOutcome.Disadvantage, GuardianStarRules.Resolve(defender, attacker));
        Assert.Equal(500, GuardianStarRules.ModifierFor(attacker, defender));
        Assert.Equal(-500, GuardianStarRules.ModifierFor(defender, attacker));
    }

    [Fact]
    public void DifferentCyclesAndNonAdjacentStarsRemainNeutral()
    {
        Assert.Equal(GuardianStarOutcome.Neutral, GuardianStarRules.Resolve("Sun", "Mars"));
        Assert.Equal(GuardianStarOutcome.Neutral, GuardianStarRules.Resolve("Sun", "Venus"));
        Assert.Equal(GuardianStarOutcome.Neutral, GuardianStarRules.Resolve("Mars", "Saturn"));
    }

    [Fact]
    public void MissingOrInvalidStarsStayUnknownInsteadOfBeingGuessed()
    {
        Assert.Equal(GuardianStarOutcome.Unknown, GuardianStarRules.Resolve(null, "Sun"));
        Assert.Equal(GuardianStarOutcome.Unknown, GuardianStarRules.Resolve("Sun", "Not a star"));
        Assert.False(GuardianStarRules.TryGetSymbol("Not a star", out var symbol));
        Assert.Equal("?", symbol);
    }

    [Theory]
    [InlineData("Sun", "☿ > ☉ > ☾")]
    [InlineData("Mars", "♆ > ♂ > ♃")]
    [InlineData("Neptune", "♇ > ♆ > ♂")]
    public void CompactChainShowsWeakSelectedAndStrongSymbols(string selected, string expected)
    {
        Assert.Equal(expected, GuardianStarRules.GetChain(selected).CompactNotation);
    }

    [Fact]
    public void NamesAreCaseInsensitiveButUnknownNamesAreRejected()
    {
        Assert.Equal("☿ > ☉ > ☾", GuardianStarRules.GetChain(" sun ").CompactNotation);
        Assert.Throws<ArgumentException>(() => GuardianStarRules.GetChain("Earth"));
    }

    [Fact]
    public void LiveAdviceUsesAttackAgainstEitherEnemyBattlePosition()
    {
        var card = TestCatalogFactory.Card(1, "Player", 2_200, 400) with
        {
            GuardianStar1 = "Sun",
            GuardianStar2 = "Mars"
        };
        var advice = GuardianStarPresentation.Create(card, 2_200, [
            new GuardianFieldTarget(1, 2_500, 3_000, GuardianBattlePosition.Attack, "Moon"),
            new GuardianFieldTarget(2, 2_500, 2_600, GuardianBattlePosition.Defense, "Moon")
        ]);

        Assert.Equal("☿ > ☉ > ☾", advice.FirstChoice);
        Assert.Equal("F1✓ F2✓", advice.FirstChoiceOutcomes);
    }

    [Fact]
    public void LiveAdvicePreservesUnknownTargetState()
    {
        var card = TestCatalogFactory.Card(1, "Player", 2_200) with
        {
            GuardianStar1 = "Sun",
            GuardianStar2 = "Mars"
        };
        var advice = GuardianStarPresentation.Create(card, 2_200, [
            new GuardianFieldTarget(3, 1_000, 1_000, GuardianBattlePosition.Unknown, null)
        ]);

        Assert.Equal("F3?", advice.FirstChoiceOutcomes);
        Assert.Equal("F3?", advice.SecondChoiceOutcomes);
    }
}
