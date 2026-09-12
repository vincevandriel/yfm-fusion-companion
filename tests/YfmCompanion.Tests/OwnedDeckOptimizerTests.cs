using YfmCompanion.Data;
using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

public sealed class OwnedDeckOptimizerTests
{
    [Fact]
    public void ProducesAnExactDeterministicLegalFortyCardDeck()
    {
        var catalog = CreateNonDragonCatalog();
        var owned = Enumerable.Range(1, 14).Select(cardId => new OwnedCardQuantity(cardId, 3)).ToArray();
        var options = new DeckOptimizationOptions(SampleHands: 24, ExactFinalists: 1, RandomSeed: 12345);
        var optimizer = new OwnedDeckOptimizer(catalog);

        var first = optimizer.Optimize(owned, options);
        var second = optimizer.Optimize(owned, options);
        var firstIds = Expand(first.Deck);
        var secondIds = Expand(second.Deck);

        Assert.Equal(40, first.TotalCards);
        Assert.Equal(658_008, first.ExactAnalysis.TotalHands);
        Assert.All(first.Deck, entry => Assert.InRange(entry.Copies, 1, 3));
        Assert.All(first.Deck, entry => Assert.True(entry.Copies <= owned.Single(item => item.CardId == entry.Card.Id).Quantity));
        Assert.Equal(firstIds, secondIds);
        Assert.Contains(first.ImportantTargets, target => target.Result.Name == "Volcanic Chimera");
        Assert.True(first.ExactAnalysis.AtLeast2800Probability > 0);
    }

    [Fact]
    public void NonDragonControlCanOptimizeACompletelyDifferentFusionFamily()
    {
        var catalog = CreateNonDragonCatalog();
        var owned = Enumerable.Range(1, 14).Select(cardId => new OwnedCardQuantity(cardId, 3));
        var options = new DeckOptimizationOptions(
            SampleHands: 16,
            ExactFinalists: 1,
            Profile: DeckStrategyProfile.FieldAndType,
            PreferredMonsterTypes: ["Pyro", "Beast"]);

        var result = new OwnedDeckOptimizer(catalog).Optimize(owned, options);

        Assert.Contains(result.Deck, entry => entry.Card.PrimaryType == "Pyro");
        Assert.Contains(result.Deck, entry => entry.Card.PrimaryType == "Beast");
        Assert.Contains(result.ImportantTargets, target => target.Result.Name == "Volcanic Chimera");
    }

    [Fact]
    public void RejectsCollectionsThatCannotSupplyFortyLegalCopies()
    {
        var catalog = CreateNonDragonCatalog();
        var owned = Enumerable.Range(1, 13).Select(cardId => new OwnedCardQuantity(cardId, 3));

        var error = Assert.Throws<ArgumentException>(() =>
            new OwnedDeckOptimizer(catalog).Optimize(owned, new DeckOptimizationOptions(SampleHands: 1, ExactFinalists: 1)));

        Assert.Contains("40 owned card copies", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CancellationIsHonoredBeforeOptimizationWorkBegins()
    {
        var catalog = CreateNonDragonCatalog();
        var owned = Enumerable.Range(1, 14).Select(cardId => new OwnedCardQuantity(cardId, 3));

        Assert.Throws<OperationCanceledException>(() =>
            new OwnedDeckOptimizer(catalog).Optimize(
                owned,
                new DeckOptimizationOptions(SampleHands: 8, ExactFinalists: 1),
                cancellationToken: new CancellationToken(canceled: true)));
    }

    [Fact]
    public void DisablingGlitchesRemovesGlitchOnlyOutcomesFromOptimization()
    {
        var cards = Enumerable.Range(1, 14)
            .Select(cardId => TestCatalogFactory.Card(cardId, $"Card {cardId}", 400, 300))
            .Append(TestCatalogFactory.Card(100, "Glitch Power", 3_000, 2_000));
        var catalog = TestCatalogFactory.Create(cards, [TestCatalogFactory.Pair(1, 2, 100, glitch: true)]);
        var owned = Enumerable.Range(1, 14).Select(cardId => new OwnedCardQuantity(cardId, 3));

        var report = new OwnedDeckOptimizer(catalog).Optimize(
            owned,
            new DeckOptimizationOptions(SampleHands: 8, ExactFinalists: 1, IncludeGlitches: false));

        Assert.Empty(report.ImportantTargets);
        Assert.Equal(0, report.ExactAnalysis.AnyFusionProbability);
    }

    [Fact]
    public void EachExodiaPieceIsRestrictedToOneCopy()
    {
        var cards = Enumerable.Range(1, 13)
            .Select(cardId => TestCatalogFactory.Card(cardId, $"Card {cardId}", 400, 300))
            .Append(TestCatalogFactory.Card(17, "Right Leg of the Forbidden One", 200, 300));
        var catalog = TestCatalogFactory.Create(cards, []);
        var owned = Enumerable.Range(1, 13)
            .Select(cardId => new OwnedCardQuantity(cardId, 3))
            .Append(new OwnedCardQuantity(17, 3));

        var report = new OwnedDeckOptimizer(catalog).Optimize(
            owned,
            new DeckOptimizationOptions(SampleHands: 4, ExactFinalists: 1));

        Assert.Equal(40, report.TotalCards);
        Assert.Equal(1, report.Deck.Single(entry => entry.Card.Id == 17).Copies);
        Assert.Contains(report.LimitedCards, item =>
            item.Card.Id == 17 && item.LimitingReason == "Exodia piece one-copy limit");
    }

    [Fact]
    public void InvalidQuantitiesAndOptionsFailBeforeExpensiveWork()
    {
        var catalog = CreateNonDragonCatalog();
        var optimizer = new OwnedDeckOptimizer(catalog);
        var enough = Enumerable.Range(1, 14).Select(cardId => new OwnedCardQuantity(cardId, 3));

        Assert.Throws<ArgumentOutOfRangeException>(() => optimizer.Optimize(
            [new OwnedCardQuantity(1, -1)],
            new DeckOptimizationOptions(SampleHands: 1, ExactFinalists: 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => optimizer.Optimize(
            enough,
            new DeckOptimizationOptions(CopyLimit: 0, SampleHands: 1, ExactFinalists: 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => optimizer.Optimize(
            enough,
            new DeckOptimizationOptions(SampleHands: 0, ExactFinalists: 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => optimizer.Optimize(
            enough,
            new DeckOptimizationOptions(SampleHands: 1, ExactFinalists: 0)));
    }

    [Fact]
    public void DuplicateOwnedRowsAreSummedBeforeCopyLimitsAreApplied()
    {
        var catalog = CreateNonDragonCatalog();
        var owned = Enumerable.Range(1, 14)
            .SelectMany(cardId => new[] { new OwnedCardQuantity(cardId, 1), new OwnedCardQuantity(cardId, 2) });

        var report = new OwnedDeckOptimizer(catalog).Optimize(
            owned,
            new DeckOptimizationOptions(SampleHands: 2, ExactFinalists: 1));

        Assert.Equal(40, report.TotalCards);
        Assert.All(report.Deck, entry => Assert.InRange(entry.Copies, 1, 3));
    }

    private static FusionCatalog CreateNonDragonCatalog()
    {
        var cards = Enumerable.Range(1, 14)
            .Select(cardId => cardId switch
            {
                1 => TestCatalogFactory.Card(cardId, "Flame Seed", 700, 500, "Pyro"),
                2 => TestCatalogFactory.Card(cardId, "Wild Cub", 650, 600, "Beast"),
                _ => TestCatalogFactory.Card(cardId, $"Filler {cardId}", 300 + cardId, 250, "Warrior")
            })
            .Append(TestCatalogFactory.Card(100, "Volcanic Chimera", 2_900, 2_300, "Pyro"));
        return TestCatalogFactory.Create(cards, [TestCatalogFactory.Pair(1, 2, 100)]);
    }

    private static int[] Expand(IEnumerable<OptimizedDeckEntry> deck) =>
        [.. deck.SelectMany(entry => Enumerable.Repeat(entry.Card.Id, entry.Copies)).Order()];
}
