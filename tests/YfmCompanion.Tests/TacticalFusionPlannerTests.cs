using YfmCompanion.Data;
using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

public sealed class TacticalFusionPlannerTests
{
    [Fact]
    public void FindsOrderedSecondaryAndTertiaryFusionChains()
    {
        var catalog = TestCatalogFactory.Create(
            [
                TestCatalogFactory.Card(1, "A", 100),
                TestCatalogFactory.Card(2, "B", 200),
                TestCatalogFactory.Card(3, "C", 300),
                TestCatalogFactory.Card(4, "D", 400),
                TestCatalogFactory.Card(10, "AB", 1_000),
                TestCatalogFactory.Card(11, "ABC", 2_000),
                TestCatalogFactory.Card(12, "ABCD", 3_000)
            ],
            [
                TestCatalogFactory.Pair(1, 2, 10),
                TestCatalogFactory.Pair(10, 3, 11),
                TestCatalogFactory.Pair(11, 4, 12)
            ]);

        var results = new TacticalFusionPlanner(catalog).FindRecommendations(
            [new(1, 1), new(2, 2), new(3, 3), new(4, 4)]);

        var strongest = Assert.Single(results, result =>
            result.FinalCard.Id == 12 && result.ConsumedHandSlots.SequenceEqual([1, 2, 3, 4]));
        Assert.Equal([1, 2, 3, 4], strongest.ConsumedHandSlots);
        Assert.Equal(3, strongest.FusionCount);
        Assert.Equal([1, 10, 11, 12], strongest.Steps.Select(step => step.Result.Id));
        Assert.Null(strongest.FieldTarget);
    }

    [Fact]
    public void InitialOrdinaryPairIsShownOnceWithLowerCardIdFirst()
    {
        var catalog = TestCatalogFactory.Create(
            [
                TestCatalogFactory.Card(1, "Lower ID"),
                TestCatalogFactory.Card(2, "Higher ID"),
                TestCatalogFactory.Card(10, "Result")
            ],
            [TestCatalogFactory.Pair(1, 2, 10)]);

        var results = new TacticalFusionPlanner(catalog).FindRecommendations(
            [new(1, 2), new(2, 1)]);

        var result = Assert.Single(results);
        Assert.Equal([2, 1], result.ConsumedHandSlots);
        Assert.DoesNotContain(results, recommendation =>
            recommendation.ConsumedHandSlots.SequenceEqual([1, 2]));
    }

    [Fact]
    public void SearchesAlternativeStartingOrders()
    {
        var catalog = TestCatalogFactory.Create(
            [
                TestCatalogFactory.Card(1, "A"), TestCatalogFactory.Card(2, "B"),
                TestCatalogFactory.Card(3, "C"), TestCatalogFactory.Card(10, "BC"),
                TestCatalogFactory.Card(11, "BCA", 2_500)
            ],
            [TestCatalogFactory.Pair(2, 3, 10), TestCatalogFactory.Pair(1, 10, 11)]);

        var results = new TacticalFusionPlanner(catalog).FindRecommendations(
            [new(1, 1), new(2, 2), new(3, 3)]);

        Assert.Contains(results, result =>
            result.FinalCard.Id == 11 && result.ConsumedHandSlots.SequenceEqual([2, 3, 1]));
    }

    [Fact]
    public void AllowsSingleHandCardToFuseOntoOneFieldTarget()
    {
        var catalog = TestCatalogFactory.Create(
            [
                TestCatalogFactory.Card(1, "Hand"), TestCatalogFactory.Card(2, "Field"),
                TestCatalogFactory.Card(10, "Result", 2_100)
            ],
            [TestCatalogFactory.Pair(1, 2, 10)]);

        var results = new TacticalFusionPlanner(catalog).FindRecommendations(
            [new(1, 1)],
            [new(FieldZone.Monster, 3, 2)]);

        var result = Assert.Single(results);
        Assert.Equal(10, result.FinalCard.Id);
        Assert.Equal(FieldZone.Monster, result.FieldTarget!.Zone);
        Assert.Equal(3, result.FieldTarget.Slot);
    }

    [Fact]
    public void FieldInteractionIsTerminalAndCannotChainThroughSecondFieldCard()
    {
        var catalog = TestCatalogFactory.Create(
            [
                TestCatalogFactory.Card(1, "Hand"), TestCatalogFactory.Card(2, "Field One"),
                TestCatalogFactory.Card(3, "Field Two"), TestCatalogFactory.Card(10, "First Result", 1_500),
                TestCatalogFactory.Card(11, "Illegal Second Result", 4_000)
            ],
            [TestCatalogFactory.Pair(1, 2, 10), TestCatalogFactory.Pair(10, 3, 11)]);

        var results = new TacticalFusionPlanner(catalog).FindRecommendations(
            [new(1, 1)],
            [new(FieldZone.Monster, 1, 2), new(FieldZone.Monster, 2, 3)]);

        Assert.Contains(results, result => result.FinalCard.Id == 10);
        Assert.DoesNotContain(results, result => result.FinalCard.Id == 11);
    }

    [Fact]
    public void OccupiedFieldCardInteractsBeforeTheSelectedHandChain()
    {
        var catalog = TestCatalogFactory.Create(
            [
                TestCatalogFactory.Card(1, "Hand One"),
                TestCatalogFactory.Card(2, "Field"),
                TestCatalogFactory.Card(3, "Hand Two"),
                TestCatalogFactory.Card(10, "Field Plus One"),
                TestCatalogFactory.Card(11, "Correct Final", 2_500),
                TestCatalogFactory.Card(12, "Hand First Result"),
                TestCatalogFactory.Card(13, "Old Reversed Result", 4_000)
            ],
            [
                TestCatalogFactory.Pair(2, 1, 10),
                TestCatalogFactory.Pair(10, 3, 11),
                TestCatalogFactory.Pair(1, 3, 12),
                TestCatalogFactory.Pair(12, 2, 13)
            ]);

        var results = new TacticalFusionPlanner(catalog).FindRecommendations(
            [new(1, 1), new(2, 3)],
            [new(FieldZone.Monster, 1, 2)]);

        Assert.Contains(results, result =>
            result.FinalCard.Id == 11 &&
            result.ConsumedHandSlots.SequenceEqual([1, 2]) &&
            result.Steps[0].Kind == TacticalStepKind.StartFromField);
        Assert.DoesNotContain(results, result =>
            result.FinalCard.Id == 13 && result.FieldTarget is not null);
    }

    [Fact]
    public void IncompatibleFieldCardDoesNotCreateDiscardOnlyRecommendation()
    {
        var catalog = TestCatalogFactory.Create(
            [
                TestCatalogFactory.Card(1, "First Hand"),
                TestCatalogFactory.Card(2, "Incompatible Field"),
                TestCatalogFactory.Card(3, "Second Hand"),
                TestCatalogFactory.Card(10, "Final", 2_000)
            ],
            [TestCatalogFactory.Pair(1, 3, 10)]);

        var results = new TacticalFusionPlanner(catalog).FindRecommendations(
            [new(1, 1), new(2, 3)],
            [new(FieldZone.Monster, 1, 2)]);

        var result = Assert.Single(results, recommendation =>
            recommendation.FinalCard.Id == 10 &&
            recommendation.FieldTarget is null &&
            recommendation.ConsumedHandSlots.SequenceEqual([1, 2]));
        Assert.DoesNotContain(results, recommendation => recommendation.FieldTarget is not null);
    }

    [Theory]
    [InlineData(75, "Man-eating Plant")]
    [InlineData(547, "Griggle")]
    public void PlantLikeMaterialRoutesDoNotCollapseQueenAndPumpkingChoices(
        int plantCardId,
        string plantName)
    {
        const int zombieWarriorId = 30;
        const int goddessId = 109;
        const int pumpkingId = 99;
        const int queenId = 638;
        var catalog = TestCatalogFactory.Create(
            [
                TestCatalogFactory.Card(zombieWarriorId, "Zombie Warrior"),
                TestCatalogFactory.Card(plantCardId, plantName),
                TestCatalogFactory.Card(goddessId, "Goddess with the Third Eye"),
                TestCatalogFactory.Card(pumpkingId, "Pumpking the King of Ghosts"),
                TestCatalogFactory.Card(queenId, "Queen of Autumn Leaves")
            ],
            [
                TestCatalogFactory.Pair(plantCardId, goddessId, queenId),
                TestCatalogFactory.Pair(zombieWarriorId, plantCardId, pumpkingId)
            ]);

        var results = new TacticalFusionPlanner(catalog).FindRecommendations(
            [new(1, plantCardId), new(2, goddessId), new(3, zombieWarriorId)]);

        var queen = Assert.Single(results, recommendation => recommendation.FinalCard.Id == queenId);
        Assert.Equal(plantCardId < goddessId ? [1, 2] : [2, 1], queen.ConsumedHandSlots);
        Assert.Equal(1, queen.FusionCount);

        var pumpking = Assert.Single(results, recommendation => recommendation.FinalCard.Id == pumpkingId);
        Assert.Equal([3, 1], pumpking.ConsumedHandSlots);
        Assert.Equal(1, pumpking.FusionCount);

        Assert.DoesNotContain(results, recommendation => recommendation.ConsumedHandSlots.Count == 3);
    }

    [Fact]
    public void DoesNotFuseFieldCardsWithoutAHandPayload()
    {
        var catalog = TestCatalogFactory.Create(
            [TestCatalogFactory.Card(1, "Field A"), TestCatalogFactory.Card(2, "Field B"), TestCatalogFactory.Card(10, "Result")],
            [TestCatalogFactory.Pair(1, 2, 10)]);

        var results = new TacticalFusionPlanner(catalog).FindRecommendations(
            [],
            [new(FieldZone.Monster, 1, 1), new(FieldZone.Monster, 2, 2)]);

        Assert.Empty(results);
    }

    [Fact]
    public void EmptyAndPartiallyFilledInputsAreAccepted()
    {
        var catalog = TestCatalogFactory.Create([TestCatalogFactory.Card(1, "A")], []);
        var planner = new TacticalFusionPlanner(catalog);

        Assert.Empty(planner.FindRecommendations([]));
        Assert.Empty(planner.FindRecommendations([new(4, 1)]));
    }

    [Fact]
    public void GlitchFusionsCanBeIncludedOrExcluded()
    {
        var catalog = TestCatalogFactory.Create(
            [TestCatalogFactory.Card(1, "A"), TestCatalogFactory.Card(2, "B"), TestCatalogFactory.Card(10, "Glitch", 9_999)],
            [TestCatalogFactory.Pair(1, 2, 10, glitch: true)]);
        var planner = new TacticalFusionPlanner(catalog);
        var hand = new[] { new HandCard(1, 1), new HandCard(2, 2) };

        Assert.NotEmpty(planner.FindRecommendations(hand, includeGlitches: true));
        Assert.Empty(planner.FindRecommendations(hand, includeGlitches: false));
    }

    [Fact]
    public void DuplicateCardsRemainDistinctBySlot()
    {
        var catalog = TestCatalogFactory.Create(
            [TestCatalogFactory.Card(1, "A"), TestCatalogFactory.Card(2, "B"), TestCatalogFactory.Card(10, "Result")],
            [TestCatalogFactory.Pair(1, 2, 10)]);

        var results = new TacticalFusionPlanner(catalog).FindRecommendations(
            [new(1, 1), new(2, 1), new(3, 2)]);

        Assert.Contains(results, result => result.ConsumedHandSlots.SequenceEqual([1, 3]));
        Assert.Contains(results, result => result.ConsumedHandSlots.SequenceEqual([2, 3]));
    }

    [Fact]
    public void StrongestResultsAreSortedFirst()
    {
        var catalog = TestCatalogFactory.Create(
            [
                TestCatalogFactory.Card(1, "A"), TestCatalogFactory.Card(2, "B"),
                TestCatalogFactory.Card(3, "C"), TestCatalogFactory.Card(10, "Weak", 1_000),
                TestCatalogFactory.Card(11, "Strong", 2_500)
            ],
            [TestCatalogFactory.Pair(1, 2, 10), TestCatalogFactory.Pair(1, 3, 11)]);

        var results = new TacticalFusionPlanner(catalog).FindRecommendations(
            [new(1, 1), new(2, 2), new(3, 3)]);

        Assert.Equal(11, results[0].FinalCard.Id);
    }

    [Fact]
    public void CompatibleEquipFromHandIsTerminalAndAddsFiveHundredStats()
    {
        var catalog = TestCatalogFactory.Create(
            [
                TestCatalogFactory.Card(1, "Monster", 1_200, 900),
                TestCatalogFactory.Card(2, "Equip")
            ],
            [],
            [(2, 1)]);

        var results = new TacticalFusionPlanner(catalog).FindRecommendations(
            [new(1, 1), new(2, 2)]);

        var result = Assert.Single(results, recommendation =>
            recommendation.ConsumedHandSlots.SequenceEqual([1, 2]));
        Assert.Equal(1, result.EquipCount);
        Assert.Equal(1_700, result.EffectiveAttack);
        Assert.Equal(1_400, result.EffectiveDefense);
        Assert.Equal(TacticalStepKind.EquipFromHand, result.Steps[^1].Kind);
        Assert.DoesNotContain(results, recommendation =>
            recommendation.ConsumedHandSlots.SequenceEqual([2, 1]));
    }

    [Fact]
    public void HandEquipCanTargetCompatibleFieldMonsterButNotAnotherFieldAfterward()
    {
        var catalog = TestCatalogFactory.Create(
            [
                TestCatalogFactory.Card(1, "Monster", 1_200, 900),
                TestCatalogFactory.Card(2, "Equip"),
                TestCatalogFactory.Card(3, "Other Field"),
                TestCatalogFactory.Card(10, "Should Never Chain", 4_000)
            ],
            [TestCatalogFactory.Pair(1, 3, 10)],
            [(2, 1)]);

        var results = new TacticalFusionPlanner(catalog).FindRecommendations(
            [new(1, 2)],
            [new(FieldZone.Monster, 1, 1), new(FieldZone.Monster, 2, 3)]);

        var result = Assert.Single(results);
        Assert.Equal(1, result.FinalCard.Id);
        Assert.Equal(1_700, result.EffectiveAttack);
        Assert.Equal(TacticalStepKind.EquipFromHand, result.Steps[^1].Kind);
        Assert.DoesNotContain(results, recommendation => recommendation.FinalCard.Id == 10);
    }

    [Fact]
    public void RejectsMoreThanFiveHandCardsAndDuplicateSlots()
    {
        var catalog = TestCatalogFactory.Create(
            Enumerable.Range(1, 6).Select(id => TestCatalogFactory.Card(id, $"Card {id}")), []);
        var planner = new TacticalFusionPlanner(catalog);

        Assert.Throws<ArgumentException>(() => planner.FindRecommendations(
            Enumerable.Range(1, 6).Select(id => new HandCard(id, id))));
        Assert.Throws<ArgumentException>(() => planner.FindRecommendations([new(1, 1), new(1, 2)]));
    }

    [Fact]
    public void RejectsOutOfRangeSlotsAndCardsInTheWrongFieldZone()
    {
        var catalog = TestCatalogFactory.Create([TestCatalogFactory.Card(1, "Card")], []);
        var planner = new TacticalFusionPlanner(catalog);

        Assert.Throws<ArgumentOutOfRangeException>(() => planner.FindRecommendations([new(0, 1)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => planner.FindRecommendations([new(6, 1)]));
        Assert.Throws<ArgumentException>(() => planner.FindRecommendations(
            [new(1, 1)],
            [new(FieldZone.SpellTrap, 1, 1)]));
        Assert.Throws<ArgumentException>(() => planner.FindRecommendations(
            [new(1, 1)],
            spellTrapFieldCards: [new(FieldZone.Monster, 1, 1)]));
    }
}
