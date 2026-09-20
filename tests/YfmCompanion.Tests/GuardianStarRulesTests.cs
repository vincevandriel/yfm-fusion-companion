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
}
