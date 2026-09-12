using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

public sealed class DeckAnalyzerTests
{
    [Fact]
    public void FortyChooseFiveMatchesExactHandCount()
    {
        Assert.Equal(658_008, DeckAnalyzer.Choose(40, 5));
    }

    [Fact]
    public void ExactFortyCardProbabilityCountsPhysicalCardCopies()
    {
        var catalog = TestCatalogFactory.Create(
            [
                TestCatalogFactory.Card(1, "Dragon"),
                TestCatalogFactory.Card(2, "Thunder"),
                TestCatalogFactory.Card(3, "Dead Draw"),
                TestCatalogFactory.Card(4, "Twin-headed Thunder Dragon", 2800, 2100)
            ],
            [TestCatalogFactory.Pair(1, 2, 4)]);
        var deck = Enumerable.Repeat(1, 3)
            .Concat(Enumerable.Repeat(2, 3))
            .Concat(Enumerable.Repeat(3, 34));

        var report = new DeckAnalyzer(catalog).Analyze(deck);
        var expectedFusingHands = DeckAnalyzer.Choose(40, 5)
                                  - (2 * DeckAnalyzer.Choose(37, 5))
                                  + DeckAnalyzer.Choose(34, 5);

        Assert.Equal(40, report.DeckSize);
        Assert.Equal(5, report.HandSize);
        Assert.Equal(658_008, report.TotalHands);
        Assert.Equal(expectedFusingHands, report.HandsWithAnyFusion);
        Assert.Equal(expectedFusingHands, report.HandsAtLeast2800);
        Assert.Equal(expectedFusingHands, report.FusionResults.Single().HandsContainingResult);
    }

    [Fact]
    public void FindsSecondaryAndTertiaryChainsAndSortsByStrength()
    {
        var catalog = TestCatalogFactory.Create(
            [
                TestCatalogFactory.Card(1, "A"),
                TestCatalogFactory.Card(2, "B"),
                TestCatalogFactory.Card(3, "C"),
                TestCatalogFactory.Card(4, "D"),
                TestCatalogFactory.Card(5, "E"),
                TestCatalogFactory.Card(10, "First", 1200),
                TestCatalogFactory.Card(11, "Second", 2200),
                TestCatalogFactory.Card(12, "Third", 3000)
            ],
            [
                TestCatalogFactory.Pair(1, 2, 10),
                TestCatalogFactory.Pair(10, 3, 11),
                TestCatalogFactory.Pair(11, 4, 12)
            ]);

        var report = new DeckAnalyzer(catalog).Analyze([1, 2, 3, 4, 5]);

        Assert.Equal(1, report.TotalHands);
        Assert.Equal([12, 11, 10], report.FusionResults.Select(result => result.Result.Id));
        Assert.Equal(4, report.FusionResults[0].RepresentativeRoute.MaterialCount);
        Assert.Equal(3_000, report.ExpectedBestFusionAttack);
    }

    [Fact]
    public void RepresentativeInitialPairUsesLowerCardIdFirstWithoutDuplicateOutcome()
    {
        var catalog = TestCatalogFactory.Create(
            [
                TestCatalogFactory.Card(1, "Lower ID"),
                TestCatalogFactory.Card(2, "Higher ID"),
                TestCatalogFactory.Card(10, "Result")
            ],
            [TestCatalogFactory.Pair(1, 2, 10)]);

        var report = new DeckAnalyzer(catalog).Analyze([2, 1]);

        var result = Assert.Single(report.FusionResults);
        Assert.Equal([1, 2], result.RepresentativeRoute.Materials.Select(card => card.Id));
        Assert.Equal(1, result.HandsContainingResult);
    }

    [Fact]
    public void GlitchFilteringAndCancellationAreExplicit()
    {
        var catalog = TestCatalogFactory.Create(
            [
                TestCatalogFactory.Card(1, "A"),
                TestCatalogFactory.Card(2, "B"),
                TestCatalogFactory.Card(3, "Glitch Result", 2500)
            ],
            [TestCatalogFactory.Pair(1, 2, 3, glitch: true)]);
        var analyzer = new DeckAnalyzer(catalog);

        Assert.Empty(analyzer.Analyze([1, 2], includeGlitches: false).FusionResults);
        Assert.True(analyzer.Analyze([1, 2], includeGlitches: true).FusionResults.Single().RepresentativeRoute.ContainsGlitch);
        Assert.Throws<OperationCanceledException>(() =>
            analyzer.Analyze([1, 2, 1, 2, 1], cancellationToken: new CancellationToken(canceled: true)));
    }

    [Fact]
    public void PartialDecksWorkAndDecksOverFortyAreRejected()
    {
        var catalog = TestCatalogFactory.Create(
            [
                TestCatalogFactory.Card(1, "A"),
                TestCatalogFactory.Card(2, "B"),
                TestCatalogFactory.Card(3, "Result")
            ],
            [TestCatalogFactory.Pair(1, 2, 3)]);
        var analyzer = new DeckAnalyzer(catalog);

        var partial = analyzer.Analyze([1, 2, 1]);

        Assert.Equal(3, partial.HandSize);
        Assert.Equal(1, partial.TotalHands);
        Assert.Throws<ArgumentException>(() => analyzer.Analyze(Enumerable.Repeat(1, 41)));
    }

    [Fact]
    public void EmptyAndSingleCardDecksProduceStableEmptyReports()
    {
        var catalog = TestCatalogFactory.Create([TestCatalogFactory.Card(1, "Only Card")], []);
        var analyzer = new DeckAnalyzer(catalog);

        var empty = analyzer.Analyze([]);
        var single = analyzer.Analyze([1]);

        Assert.Equal((0, 0, 0), (empty.DeckSize, empty.HandSize, empty.TotalHands));
        Assert.Equal((1, 1, 0), (single.DeckSize, single.HandSize, single.TotalHands));
        Assert.Empty(empty.FusionResults);
        Assert.Empty(single.FusionResults);
    }

    [Fact]
    public void UnknownCardsAndInvalidCombinationsAreRejected()
    {
        var catalog = TestCatalogFactory.Create([TestCatalogFactory.Card(1, "Known")], []);
        var analyzer = new DeckAnalyzer(catalog);

        Assert.Throws<KeyNotFoundException>(() => analyzer.Analyze([1, 722]));
        Assert.Equal(0, DeckAnalyzer.Choose(-1, 0));
        Assert.Equal(0, DeckAnalyzer.Choose(4, 5));
        Assert.Equal(1, DeckAnalyzer.Choose(0, 0));
    }

    [Fact]
    public void CompatibleEquipCountsAsTerminalFusionOpportunity()
    {
        var catalog = TestCatalogFactory.Create(
            [
                TestCatalogFactory.Card(1, "Monster", 1_600, 1_000),
                TestCatalogFactory.Card(2, "Equip"),
                TestCatalogFactory.Card(3, "Other")
            ],
            [],
            [(2, 1)]);

        var report = new DeckAnalyzer(catalog).Analyze([2, 1, 3]);
        var equipped = Assert.Single(report.FusionResults);

        Assert.True(equipped.IsEquipped);
        Assert.Equal(2_100, equipped.EffectiveAttack);
        Assert.Equal(1_500, equipped.EffectiveDefense);
        Assert.Equal(1, report.HandsWithAnyFusion);
        Assert.Equal(1, report.HandsAtLeast2000);
        Assert.True(equipped.RepresentativeRoute.EndsWithEquip);
        Assert.Equal([1, 2], equipped.RepresentativeRoute.Materials.Select(card => card.Id));
    }

    [Fact]
    public void EquipBonusAppliesAfterFinalFusionAndNeverCarriesThroughOne()
    {
        var catalog = TestCatalogFactory.Create(
            [
                TestCatalogFactory.Card(1, "Monster", 1_500),
                TestCatalogFactory.Card(2, "Fusion Material"),
                TestCatalogFactory.Card(3, "Equip"),
                TestCatalogFactory.Card(4, "Fusion Result", 2_500)
            ],
            [TestCatalogFactory.Pair(1, 2, 4)],
            [(3, 1), (3, 4)]);

        var report = new DeckAnalyzer(catalog).Analyze([1, 2, 3]);
        var plainFusion = Assert.Single(report.FusionResults, result => result.Result.Id == 4 && !result.IsEquipped);
        var finalEquip = Assert.Single(report.FusionResults, result => result.Result.Id == 4 && result.IsEquipped);

        Assert.Equal(2_500, plainFusion.EffectiveAttack);
        Assert.Equal(3_000, finalEquip.EffectiveAttack);
        Assert.Equal(3, finalEquip.RepresentativeRoute.MaterialCount);
        Assert.True(finalEquip.RepresentativeRoute.EndsWithEquip);
        Assert.Equal(3_000, report.ExpectedBestFusionAttack);
    }

    [Fact]
    public void MegamorphUsesItsDocumentedOneThousandPointTerminalBonus()
    {
        var catalog = TestCatalogFactory.Create(
            [
                TestCatalogFactory.Card(1, "Monster", 1_600, 1_200),
                TestCatalogFactory.Card(2, "Other"),
                TestCatalogFactory.Card(657, "Megamorph", primaryType: "Equip")
            ],
            [],
            [(657, 1)]);

        var report = new DeckAnalyzer(catalog).Analyze([1, 2, 657]);
        var equipped = Assert.Single(report.FusionResults);

        Assert.Equal(1_000, equipped.AttackBonus);
        Assert.Equal(2_600, equipped.EffectiveAttack);
        Assert.Equal(2_200, equipped.EffectiveDefense);
        Assert.Equal(1_000, equipped.RepresentativeRoute.EquipBonus);
    }
}
