using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public sealed class CardSearchService(IEnumerable<Card> cards)
{
    private readonly Card[] _cards = [.. cards.OrderBy(card => card.Id)];

    public IReadOnlyList<Card> Search(string? query, int maximumResults = 12)
    {
        if (maximumResults <= 0 || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var term = query.Trim();
        return [.. _cards
            .Select(card => new { Card = card, Rank = Rank(card, term) })
            .Where(item => item.Rank < 4)
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.Card.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Card.Id)
            .Take(maximumResults)
            .Select(item => item.Card)];
    }

    private static int Rank(Card card, string term)
    {
        var id = card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (card.Name.Equals(term, StringComparison.OrdinalIgnoreCase) || id == term)
        {
            return 0;
        }

        if (card.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (id.StartsWith(term, StringComparison.Ordinal))
        {
            return 2;
        }

        return card.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ? 3 : 4;
    }
}
