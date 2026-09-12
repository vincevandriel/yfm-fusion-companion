using YfmCompanion.Engine;

namespace YfmCompanion.Tests;

public sealed class CardSearchServiceTests
{
    private readonly CardSearchService _search = new(
        [
            TestCatalogFactory.Card(1, "Blue-eyes White Dragon"),
            TestCatalogFactory.Card(2, "Baby Dragon"),
            TestCatalogFactory.Card(20, "Dragon Piper"),
            TestCatalogFactory.Card(200, "Petit Dragon"),
            TestCatalogFactory.Card(613, "Twin-headed Thunder Dragon")
        ]);

    [Fact]
    public void PrefixMatchesRankBeforeSubstringMatches()
    {
        var results = _search.Search("dragon");

        Assert.Equal("Dragon Piper", results[0].Name);
        Assert.Contains(results, card => card.Name == "Petit Dragon");
    }

    [Fact]
    public void ExactNameAndCardNumberRankFirst()
    {
        Assert.Equal("Petit Dragon", _search.Search("Petit Dragon")[0].Name);
        Assert.Equal("Twin-headed Thunder Dragon", _search.Search("613")[0].Name);
    }

    [Fact]
    public void SearchIsCaseInsensitiveAndBounded()
    {
        Assert.Equal(2, _search.Search("DRAGON", maximumResults: 2).Count);
        Assert.Empty(_search.Search(""));
    }

    [Fact]
    public void WhitespaceAndNonPositiveLimitsReturnNoSuggestions()
    {
        Assert.Empty(_search.Search("   "));
        Assert.Empty(_search.Search("dragon", maximumResults: 0));
        Assert.Empty(_search.Search("dragon", maximumResults: -1));
    }

    [Fact]
    public void NumericPrefixResultsRemainDeterministic()
    {
        Assert.Equal([20, 200], _search.Search("20").Select(card => card.Id));
    }
}
