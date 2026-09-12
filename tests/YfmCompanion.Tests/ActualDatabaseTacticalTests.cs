using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class ActualDatabaseTacticalTests(DatabaseFixture fixture)
{
    [Fact]
    public void RealThreeCardChainProducesTwinHeadedThunderDragon()
    {
        var petitDragon = fixture.Catalog.GetCard("Petit Dragon");
        var immortalOfThunder = fixture.Catalog.GetCard("The Immortal of Thunder");
        var babyDragon = fixture.Catalog.GetCard("Baby Dragon");
        var planner = new TacticalFusionPlanner(fixture.Catalog);

        var results = planner.FindRecommendations(
            [new(1, petitDragon.Id), new(2, immortalOfThunder.Id), new(3, babyDragon.Id)]);

        var result = Assert.Single(results, recommendation =>
            recommendation.FinalCard.Name == "Twin-headed Thunder Dragon" &&
            recommendation.ConsumedHandSlots.SequenceEqual([1, 2, 3]));
        Assert.Equal(2, result.FusionCount);
        Assert.Equal("Thunder Dragon", result.Steps[1].Result.Name);
        Assert.Equal("Twin-headed Thunder Dragon", result.Steps[2].Result.Name);
        Assert.Equal(2_800, result.FinalCard.Attack);
        Assert.Equal(2_100, result.FinalCard.Defense);
    }

    [Fact]
    public void RealFieldCardFusesBeforeTheOrderedHandChain()
    {
        var petitDragon = fixture.Catalog.GetCard("Petit Dragon");
        var immortalOfThunder = fixture.Catalog.GetCard("The Immortal of Thunder");
        var babyDragon = fixture.Catalog.GetCard("Baby Dragon");
        var planner = new TacticalFusionPlanner(fixture.Catalog);

        var results = planner.FindRecommendations(
            [new(1, petitDragon.Id), new(2, immortalOfThunder.Id)],
            [new(FieldZone.Monster, 4, babyDragon.Id)]);

        var result = Assert.Single(results, recommendation =>
            recommendation.FinalCard.Name == "Twin-headed Thunder Dragon" &&
            recommendation.ConsumedHandSlots.SequenceEqual([2, 1]) &&
            recommendation.FieldTarget?.Slot == 4);
        Assert.Equal(2, result.FusionCount);
        Assert.Equal(TacticalStepKind.StartFromField, result.Steps[0].Kind);
        Assert.Equal("Thunder Dragon", result.Steps[1].Result.Name);
        Assert.Equal("Twin-headed Thunder Dragon", result.Steps[2].Result.Name);
    }

    [Fact]
    public void RealNonFusionDoesNotAppearInRecommendations()
    {
        var midnightFiend = fixture.Catalog.GetCard("Midnight Fiend");
        var yashinoki = fixture.Catalog.GetCard("Yashinoki");
        var planner = new TacticalFusionPlanner(fixture.Catalog);

        var results = planner.FindRecommendations([new(1, midnightFiend.Id), new(2, yashinoki.Id)]);

        Assert.Empty(results);
    }

    [Fact]
    public void RealEquipCompatibilityProducesAStatAdjustedRecommendation()
    {
        var axeOfDespair = fixture.Catalog.GetCard("Axe of Despair");
        var battleOx = fixture.Catalog.GetCard("Battle Ox");
        var planner = new TacticalFusionPlanner(fixture.Catalog);

        var results = planner.FindRecommendations(
            [new(1, axeOfDespair.Id)],
            [new(FieldZone.Monster, 2, battleOx.Id)]);

        var result = Assert.Single(results);
        Assert.Equal("Battle Ox", result.FinalCard.Name);
        Assert.Equal(battleOx.Attack + 500, result.EffectiveAttack);
        Assert.Equal(battleOx.Defense + 500, result.EffectiveDefense);
        Assert.Equal(TacticalStepKind.EquipFromHand, result.Steps[^1].Kind);
    }

    [Theory]
    [InlineData("Mystic Lamp")]
    [InlineData("Phantom Dewan")]
    [InlineData("Sectarian of Secrets")]
    public void MonsturtleAndRecommendedDeckSpellcasterCannotBeReportedAsUshiOni(string spellcasterName)
    {
        var monsturtle = fixture.Catalog.GetCard("Monsturtle");
        var spellcaster = fixture.Catalog.GetCard(spellcasterName);
        var results = new TacticalFusionPlanner(fixture.Catalog).FindRecommendations(
            [new(1, monsturtle.Id), new(2, spellcaster.Id)]);

        var result = Assert.Single(results);
        Assert.Equal("30,000-Year White Turtle", result.FinalCard.Name);
        Assert.DoesNotContain(results, recommendation => recommendation.FinalCard.Name == "Ushi Oni");
    }

    [Theory]
    [InlineData("Ancient Jar", "Mystic Lamp")]
    [InlineData("Ancient Jar", "Phantom Dewan")]
    [InlineData("Ancient Jar", "Sectarian of Secrets")]
    [InlineData("Pot the Trick", "Mystic Lamp")]
    [InlineData("Pot the Trick", "Phantom Dewan")]
    [InlineData("Pot the Trick", "Sectarian of Secrets")]
    public void RecommendedDeckUshiOniPairsRemainExact(string rockName, string spellcasterName)
    {
        var rock = fixture.Catalog.GetCard(rockName);
        var spellcaster = fixture.Catalog.GetCard(spellcasterName);
        var results = new TacticalFusionPlanner(fixture.Catalog).FindRecommendations(
            [new(1, rock.Id), new(2, spellcaster.Id)]);

        var result = Assert.Single(results);
        Assert.Equal("Ushi Oni", result.FinalCard.Name);
    }
}
