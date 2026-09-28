using YfmCompanion.Data;
using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class FreeDuelReferenceDataTests(DatabaseFixture fixture)
{
    [Fact]
    public void BundledDataContainsEveryExactRewardTableAndOrderedCardSources()
    {
        var data = FreeDuelReferenceData.LoadBundled(AppContext.BaseDirectory, fixture.Catalog);
        Assert.Equal(39, data.Duelists.Count);
        Assert.Equal(117, data.Duelists.Sum(d => d.RewardTables.Count));
        Assert.Equal(8_666, data.Duelists.Sum(d => d.RewardTables.Sum(t => t.Drops.Count)));
        Assert.All(data.Duelists.SelectMany(d => d.RewardTables), table =>
        {
            Assert.Equal(2_048, table.Drops.Sum(drop => drop.Weight));
            Assert.Equal(table.Drops.OrderByDescending(drop => drop.Weight).ThenBy(drop => drop.CardId), table.Drops);
        });

        var meadowMage = data.Duelists.Single(d => d.Id == 29);
        Assert.Equal("Meadow Mage", meadowMage.Name);
        Assert.Equal("29.png", meadowMage.PortraitFile);
        var meteor = data.DropsForCard(713);
        Assert.NotEmpty(meteor);
        Assert.Equal(meteor.OrderByDescending(drop => drop.Probability)
            .ThenBy(drop => drop.DuelistName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(drop => drop.TableLabel, StringComparer.OrdinalIgnoreCase), meteor);
        Assert.Equal(meteor[0], data.BestFarmForCard(713));
        Assert.InRange(meteor[0].Probability, 0.00001, 1);
    }

    [Fact]
    public void ACardWithoutRewardsHasNoInventedFarmSource()
    {
        var data = FreeDuelReferenceData.LoadBundled(AppContext.BaseDirectory, fixture.Catalog);
        var unavailable = fixture.Catalog.Cards.First(card => data.DropsForCard(card.Id).Count == 0);
        Assert.Null(data.BestFarmForCard(unavailable.Id));
    }

    [Fact]
    public void LoaderRejectsATableWhoseDeclaredDenominatorOrWeightTotalChanges()
    {
        var source = Path.Combine(RuntimeResources.FindRoot(AppContext.BaseDirectory), CampaignResearchData.BundledDirectoryName,
            FreeDuelReferenceData.FileName);
        var directory = Directory.CreateTempSubdirectory("yfm-free-duel-");
        try
        {
            var invalid = Path.Combine(directory.FullName, FreeDuelReferenceData.FileName);
            var json = File.ReadAllText(source).ReplaceFirst("\"denominator\": 2048", "\"denominator\": 2047");
            File.WriteAllText(invalid, json);
            Assert.Throws<InvalidDataException>(() => FreeDuelReferenceData.Load(invalid, fixture.Catalog));
        }
        finally { directory.Delete(recursive: true); }
    }
}

file static class StringTestExtensions
{
    public static string ReplaceFirst(this string source, string oldValue, string newValue)
    {
        var index = source.IndexOf(oldValue, StringComparison.Ordinal);
        Assert.True(index >= 0, "Expected generator formatting was not found in the fixture.");
        return string.Concat(source.AsSpan(0, index), newValue, source.AsSpan(index + oldValue.Length));
    }
}
