using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class ActualDatabaseDeckAnalysisTests(DatabaseFixture fixture)
{
    [Fact]
    public void RealFortyCardDeckEnumeratesEveryPhysicalFiveCardHand()
    {
        var petitDragon = fixture.Catalog.GetCard("Petit Dragon").Id;
        var immortalOfThunder = fixture.Catalog.GetCard("The Immortal of Thunder").Id;
        var babyDragon = fixture.Catalog.GetCard("Baby Dragon").Id;
        var skullServant = fixture.Catalog.GetCard("Skull Servant").Id;
        var deck = Enumerable.Repeat(petitDragon, 10)
            .Concat(Enumerable.Repeat(immortalOfThunder, 10))
            .Concat(Enumerable.Repeat(babyDragon, 10))
            .Concat(Enumerable.Repeat(skullServant, 10));

        var report = new DeckAnalyzer(fixture.Catalog).Analyze(deck);

        Assert.Equal(658_008, report.TotalHands);
        Assert.Contains(report.FusionResults, result => result.Result.Name == "Twin-headed Thunder Dragon");
        Assert.True(report.AtLeast2800Probability > 0);
    }
}
