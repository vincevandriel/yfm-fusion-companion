using YfmCompanion.Data;
using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

public sealed class StarChipDeckPlannerTests
{
    [Fact]
    public void DisabledModeUsesOnlyOwnedCardsAndSpendsNothing()
    {
        var catalog = CreateCatalog();
        var owned = Enumerable.Range(1, 14).Select(id => new OwnedCardQuantity(id, 3));

        var plan = new StarChipDeckPlanner(catalog).Plan(
            owned,
            999,
            useStarChips: false,
            FastOptions());

        Assert.False(plan.StarChipsEnabled);
        Assert.Equal(0U, plan.SpentStarChips);
        Assert.Equal(999U, plan.RemainingStarChips);
        Assert.Empty(plan.Purchases);
        Assert.Equal(40, plan.ResultingDeck.TotalCards);
    }

    [Fact]
    public void EnabledModeBuysOnlyCopiesUsedByTheResultingLegalDeck()
    {
        var catalog = CreateCatalog();
        var owned = Enumerable.Range(1, 13).Select(id => new OwnedCardQuantity(id, 3));

        var plan = new StarChipDeckPlanner(catalog).Plan(
            owned,
            100,
            useStarChips: true,
            FastOptions());

        Assert.True(plan.StarChipsEnabled);
        var purchase = Assert.Single(plan.Purchases);
        Assert.Equal(14, purchase.Card.Id);
        Assert.Equal(1, purchase.Copies);
        Assert.Equal(100U, plan.SpentStarChips);
        Assert.Equal(0U, plan.RemainingStarChips);
        Assert.Contains(plan.ResultingDeck.Deck, entry => entry.Card.Id == 14 && entry.Copies == 1);
        Assert.Contains("not a proof", plan.Methodology, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InsufficientBudgetFailsWithoutInventingOwnedCards()
    {
        var catalog = CreateCatalog();
        var owned = Enumerable.Range(1, 13).Select(id => new OwnedCardQuantity(id, 3));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new StarChipDeckPlanner(catalog).Plan(owned, 99, useStarChips: true, FastOptions()));

        Assert.Contains("cannot supply 40 legal", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PasswordShopPlanPurchasesEachCardNameAtMostOnce()
    {
        var catalog = CreateCatalog(cardCount: 17);
        var owned = Enumerable.Range(1, 12).Select(id => new OwnedCardQuantity(id, 3));

        var plan = new StarChipDeckPlanner(catalog).Plan(
            owned,
            400,
            useStarChips: true,
            FastOptions());

        Assert.Equal(4, plan.Purchases.Count);
        Assert.All(plan.Purchases, purchase => Assert.Equal(1, purchase.Copies));
        Assert.Equal(
            plan.Purchases.Count,
            plan.Purchases.Select(purchase => purchase.Card.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(400U, plan.SpentStarChips);
        Assert.Equal(40, plan.ResultingDeck.TotalCards);
        var deckCounts = plan.ResultingDeck.Deck.ToDictionary(entry => entry.Card.Id, entry => entry.Copies);
        Assert.All(plan.Purchases, purchase =>
            Assert.True(deckCounts.GetValueOrDefault(purchase.Card.Id) >= purchase.Copies));
    }

    [Fact]
    public void InvalidAndAlreadyRedeemedPasswordsAreNeverRecommended()
    {
        var baseCatalog = CreateCatalog(cardCount: 16);
        var cards = baseCatalog.Cards.Select(card => card.Id switch
        {
            14 => card with { Password = "abcdefgh" },
            _ => card
        }).ToArray();
        var catalog = TestCatalogFactory.Create(cards, []);
        var owned = Enumerable.Range(1, 13).Select(id => new OwnedCardQuantity(id, 3));
        var options = FastOptions() with
        {
            AlreadyRedeemedCardNames = new HashSet<string>(["Card 15"], StringComparer.OrdinalIgnoreCase)
        };

        var plan = new StarChipDeckPlanner(catalog).Plan(owned, 100, useStarChips: true, options);

        var purchase = Assert.Single(plan.Purchases);
        Assert.Equal(16, purchase.Card.Id);
        Assert.All(purchase.Card.Password!, character => Assert.True(char.IsAsciiDigit(character)));
    }

    private static DeckOptimizationOptions FastOptions() =>
        new(SampleHands: 1, ExactFinalists: 1, IncludeGlitches: false);

    private static FusionCatalog CreateCatalog(int cardCount = 14)
    {
        var cards = Enumerable.Range(1, cardCount)
            .Select(id => new Card(
                id,
                $"Card {id}",
                null,
                "Sun",
                "Mars",
                4,
                "Test",
                null,
                id >= 14 ? 2500 + id : 100 + id,
                100,
                $"{id:00000000}",
                id >= 14 ? 100 : 999999,
                true,
                true,
                true))
            .ToArray();
        return TestCatalogFactory.Create(cards, []);
    }
}
