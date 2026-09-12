using YfmCompanion.Data;

namespace YfmCompanion.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class FusionCatalogTests(DatabaseFixture fixture)
{
    private static readonly string[] RuleKinds = ["general", "exact"];

    [Fact]
    public void LoadsCompleteCardAndPairCatalog()
    {
        Assert.Equal(722, fixture.Catalog.Cards.Count);
        Assert.Equal(25_146, fixture.Catalog.FusionPairs.Count);
    }

    [Fact]
    public void PairResolutionIsOrderIndependent()
    {
        var pair = fixture.Catalog.FusionPairs.First();

        var forward = fixture.Catalog.Resolve(pair.MaterialLowId, pair.MaterialHighId);
        var reverse = fixture.Catalog.Resolve(pair.MaterialHighId, pair.MaterialLowId);

        Assert.NotNull(forward);
        Assert.NotNull(reverse);
        Assert.Equal(forward.Result.Id, reverse.Result.Id);
        Assert.Equal(pair.ResultCardId, forward.Result.Id);
    }

    [Fact]
    public void RejectsNonCanonicalOrDuplicateFusionPairRows()
    {
        var cards = new[]
        {
            TestCatalogFactory.Card(1, "A"),
            TestCatalogFactory.Card(2, "B"),
            TestCatalogFactory.Card(10, "Result")
        };

        Assert.Throws<InvalidDataException>(() => new FusionCatalog(
            cards,
            [new FusionPair(2, 1, 10, true, false)]));
        Assert.Throws<InvalidDataException>(() => new FusionCatalog(
            cards,
            [
                new FusionPair(1, 2, 10, true, false),
                new FusionPair(1, 2, 10, true, false)
            ]));
    }

    [Fact]
    public void UnknownCardIdsAreRejectedInsteadOfTreatedAsNoFusion()
    {
        Assert.Throws<KeyNotFoundException>(() => fixture.Catalog.Resolve(0, 1));
        Assert.Throws<KeyNotFoundException>(() => fixture.Catalog.Resolve(1, 723));
    }

    [Fact]
    public void NonFusionReturnsNull()
    {
        Assert.Null(fixture.Catalog.Resolve("Petit Dragon", "Petit Dragon"));
    }

    [Fact]
    public void GlitchFilteringIsExplicit()
    {
        var glitch = fixture.Catalog.FusionPairs.First(pair => pair.IsGlitch);

        Assert.NotNull(fixture.Catalog.Resolve(glitch.MaterialLowId, glitch.MaterialHighId, includeGlitches: true));
        Assert.Null(fixture.Catalog.Resolve(glitch.MaterialLowId, glitch.MaterialHighId, includeGlitches: false));
    }

    [Fact]
    public void RuleProvenanceIsAvailableForExpandedPairs()
    {
        var pair = fixture.Catalog.FusionPairs.First(pair => pair.IsIntended);
        var result = fixture.Catalog.Resolve(pair.MaterialLowId, pair.MaterialHighId);

        Assert.NotNull(result);
        Assert.NotEmpty(result.RuleReferences);
        Assert.All(result.RuleReferences, reference => Assert.Contains(reference.RuleKind, RuleKinds));
    }

    [Theory]
    [InlineData("Petit Dragon", "The Immortal of Thunder", "Thunder Dragon")]
    [InlineData("Baby Dragon", "Thunder Dragon", "Twin-headed Thunder Dragon")]
    [InlineData("Air Marmot of Nefariousness", "Greenkappa", "Tiger Axe")]
    [InlineData("Dissolverock", "Skull Servant", "Stone Ghost")]
    public void VerifiedGoldenPairsMatchTheSuppliedDatabase(
        string first,
        string second,
        string expectedResult)
    {
        var resolution = fixture.Catalog.Resolve(first, second);

        Assert.NotNull(resolution);
        Assert.Equal(expectedResult, resolution.Result.Name);
        Assert.False(resolution.IsGlitch);
    }

    [Fact]
    public void VerifiedAbsentDisputedPairsRemainAbsent()
    {
        Assert.Null(fixture.Catalog.Resolve("Midnight Fiend", "Yashinoki"));
        Assert.Null(fixture.Catalog.Resolve("Bone Mouse", "Skull Servant"));
    }

    [Fact]
    public void NamedPlantQueenAndPumpkingRoutesRemainExactAndSeparate()
    {
        Assert.Null(fixture.Catalog.Resolve("Man-eating Plant", "Griggle"));
        Assert.Equal(
            "Queen of Autumn Leaves",
            fixture.Catalog.Resolve("Man-eating Plant", "Goddess with the Third Eye")!.Result.Name);
        Assert.Equal(
            "Queen of Autumn Leaves",
            fixture.Catalog.Resolve("Griggle", "Goddess with the Third Eye")!.Result.Name);
        Assert.Equal(
            "Pumpking the King of Ghosts",
            fixture.Catalog.Resolve("Man-eating Plant", "Zombie Warrior")!.Result.Name);
        Assert.Equal(
            "Pumpking the King of Ghosts",
            fixture.Catalog.Resolve("Griggle", "Zombie Warrior")!.Result.Name);
        Assert.Null(fixture.Catalog.Resolve("Queen of Autumn Leaves", "Zombie Warrior"));
    }

    [Fact]
    public void VerifiedGlitchPairIsMarkedAndFilterable()
    {
        var resolution = fixture.Catalog.Resolve("Ancient Jar", "Fiend Sword");

        Assert.NotNull(resolution);
        Assert.Equal("Tiger Axe", resolution.Result.Name);
        Assert.True(resolution.IsGlitch);
        Assert.Null(fixture.Catalog.Resolve("Ancient Jar", "Fiend Sword", includeGlitches: false));
    }

    [Fact]
    public void EquipCompatibilityIsImportedSeparatelyFromFusionPairs()
    {
        var equip = fixture.Catalog.GetCard("Axe of Despair");
        var compatibleMonster = fixture.Catalog.GetCard("Battle Ox");
        var incompatibleMonster = fixture.Catalog.GetCard("Petit Dragon");

        Assert.True(fixture.Catalog.CanEquip(equip.Id, compatibleMonster.Id));
        Assert.False(fixture.Catalog.CanEquip(equip.Id, incompatibleMonster.Id));
    }
}
