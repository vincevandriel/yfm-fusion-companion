using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class ActualDatabaseOptimizerTests(DatabaseFixture fixture)
{
    [Fact]
    public void FullOwnedCatalogProducesARealLegalAndStrategicallyFilteredDeck()
    {
        var owned = fixture.Catalog.Cards.Select(card => new OwnedCardQuantity(card.Id, 3));
        var options = new DeckOptimizationOptions(SampleHands: 48, ExactFinalists: 2, RandomSeed: 0x59464D);

        var report = new OwnedDeckOptimizer(fixture.Catalog).Optimize(owned, options);

        Assert.Equal(40, report.TotalCards);
        Assert.Equal(658_008, report.ExactAnalysis.TotalHands);
        Assert.All(report.Deck, entry => Assert.InRange(entry.Copies, 1, 3));
        Assert.DoesNotContain(report.Deck, entry => entry.Card.Id == 690);
        Assert.DoesNotContain(report.Deck, entry => entry.Card.PrimaryType.Equals("Ritual", StringComparison.OrdinalIgnoreCase));
        Assert.True(report.ExactAnalysis.AtLeast2800Probability > 0);
        Assert.NotEmpty(report.ImportantTargets);
    }
}
