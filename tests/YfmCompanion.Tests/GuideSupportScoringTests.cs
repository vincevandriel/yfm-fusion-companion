using YfmCompanion.Data;
using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

public sealed class GuideSupportScoringTests
{
    private static FusionCatalog Catalog(bool incompatible = false) => TestCatalogFactory.Create(
        [TestCatalogFactory.Card(1, "Dragon", 1200), TestCatalogFactory.Card(2, "Thunder", 1600),
         TestCatalogFactory.Card(10, "Fusion", 2800, primaryType: "Thunder"),
         TestCatalogFactory.Card(3, "Equip A", primaryType: "Equip"), TestCatalogFactory.Card(4, "Equip B", primaryType: "Equip"),
         TestCatalogFactory.Card(334, "Umi", primaryType: "Magic"), TestCatalogFactory.Card(336, "Dark Hole", primaryType: "Magic"),
         TestCatalogFactory.Card(337, "Raigeki", primaryType: "Magic"), TestCatalogFactory.Card(657, "Megamorph", primaryType: "Equip")],
        [TestCatalogFactory.Pair(1, 2, 10), TestCatalogFactory.Pair(1, 3, 10)],
        incompatible ? [] : [(3, 10), (4, 10), (657, 10)]);

    [Fact]
    public void StructureBonusRequiresCompatibilityWithOneEstablishedTarget()
    {
        var catalog = Catalog();
        var analyzer = new DeckAnalyzer(catalog);
        int[] deck = [1, 2, 3, 4, 334];
        var support = GuideSupportStructure.Evaluate(catalog, deck, analyzer.Analyze(deck, false));
        Assert.True(support.MeetsSupportTarget);
        Assert.Equal(9, support.Points);
        Assert.Equal(2, support.CompatibleEquipCopies);
        Assert.Equal(1, support.UsefulFieldCopies);
        var unrelated = Catalog(true);
        var absent = GuideSupportStructure.Evaluate(unrelated, deck, new DeckAnalyzer(unrelated).Analyze(deck, false));
        Assert.False(absent.MeetsSupportTarget);
        Assert.Equal(0, absent.CompatibleEquipCopies);
    }

    [Fact]
    public void SupportRankIsTransitiveAcrossDifferentHandTotalsAndSupportCounts()
    {
        var comparer = new DeckObjectiveComparer(false, false);
        var candidates = (from total in new long[] { 160, 320, 658008 }
                          from count in new[] { 0, 1, 2 }
                          from structure in new[] { 0, 6, 9, 10 }
                          select new DeckObjective(new(40, 5, total, count, count, count, count, count, 0, [])
                          { HandsWith3500Setup = count, HandsWith3500SetupOrBoardClear = count, HandsWithBroadRemoval = count }, null, null, [1])
                          { GuideStructurePoints = structure }).ToArray();
        foreach (var a in candidates)
            foreach (var b in candidates)
            {
                Assert.Equal(Math.Sign(comparer.Compare(a, b)), -Math.Sign(comparer.Compare(b, a)));
                foreach (var c in candidates)
                    if (comparer.Compare(a, b) > 0 && comparer.Compare(b, c) > 0) Assert.True(comparer.Compare(a, c) > 0);
            }
    }

    [Fact]
    public void TerrainAndTwoPhysicalEquipsCreateSetupWithoutChangingImmediateFusionMetrics()
    {
        var result = new DeckAnalyzer(Catalog()).Analyze([1, 2, 3, 4, 334], false);
        Assert.Equal(1, result.TotalHands);
        Assert.Equal(1, result.HandsWith3500Setup);
        Assert.Equal(0, result.HandsWith4500Setup);
        Assert.Equal(4300, result.MaximumSetupAttack);
        Assert.Equal(3300, result.FusionResults.Max(r => r.EffectiveAttack));
        Assert.Equal(0, new DeckAnalyzer(Catalog(true)).Analyze([1, 2, 3, 4, 334], false).HandsWith3500Setup);
    }

    [Fact]
    public void EquipConsumedAsFusionMaterialCannotAlsoPowerUpItsResult()
    {
        var result = new DeckAnalyzer(Catalog()).Analyze([1, 3, 334], false);
        Assert.Equal(3300, result.MaximumSetupAttack);
        Assert.Equal(0, result.HandsWith3500Setup);
    }

    [Fact]
    public void DuplicateMegamorphsStackOncePerCopyAndTerrainIsNotStacked()
    {
        var stacked = new DeckAnalyzer(Catalog()).Analyze([1, 2, 657, 657, 334], false);
        Assert.Equal(5300, stacked.MaximumSetupAttack);
        Assert.Equal(1, stacked.HandsWith4500Setup);
        var fields = new DeckAnalyzer(Catalog()).Analyze([1, 2, 3, 334, 334], false);
        Assert.Equal(3800, fields.MaximumSetupAttack);
    }

    [Fact]
    public void SetupOrClearUnionNeverDoubleCountsAndCountsExactPhysicalHands()
    {
        var result = new DeckAnalyzer(Catalog()).Analyze([1, 2, 3, 4, 334, 336], false);
        Assert.Equal(6, result.TotalHands);
        Assert.Equal(5, result.HandsWithBoardClear);
        Assert.Equal(6, result.HandsWith3500SetupOrBoardClear);
        Assert.InRange(result.HandsWith3500Setup, 1, 6);
    }

    [Fact]
    public void BalancedRankingRewardsSupportAndRemovalInsteadOfDiscardingTheirSeedValues()
    {
        var analyzer = new DeckAnalyzer(Catalog());
        var supported = analyzer.Analyze([1, 2, 3, 4, 334], false);
        var materials = analyzer.Analyze([1, 1, 2, 2, 2], false);
        var comparer = new DeckObjectiveComparer(false, false);
        Assert.True(comparer.Compare(new(supported, null, null, [1, 2, 3, 4, 334]), new(materials, null, null, [1, 1, 2, 2, 2])) > 0);
        var clear = analyzer.Analyze([1, 1, 2, 2, 336], false);
        Assert.True(comparer.Compare(new(clear, null, null, [1, 1, 2, 2, 336]), new(materials, null, null, [1, 1, 2, 2, 2])) > 0);
    }

    [Fact]
    public void DarkHoleCountsAsRecoveryButRaigekiRemainsBetterAndCrushCardIsConditional()
    {
        var target = new DeckSafetyTarget(1, "Opponent", 20, "Threat", 4500, [], 1, false);
        var wipe = OpponentSafetyScoring.CounterValueForTarget(Catalog().GetCard(336), target);
        Assert.True(wipe > 0);
        Assert.True(OpponentSafetyScoring.CounterValueForTarget(Catalog().GetCard(337), target) > wipe);
        var crush = TestCatalogFactory.Card(661, "Crush Card");
        Assert.Equal(0, OpponentSafetyScoring.CounterValueForTarget(crush, target with { Attack = 1400 }));
        Assert.True(OpponentSafetyScoring.CounterValueForTarget(crush, target) > 0);
    }
}
