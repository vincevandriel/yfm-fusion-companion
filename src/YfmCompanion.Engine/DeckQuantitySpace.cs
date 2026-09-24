using System.Numerics;
using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public sealed record DeckQuantityCandidate(int[]? Cards, long RequiredStarChips, BigInteger NextOrdinal);
public sealed record DeckQuantityCapacity(int CardId, int Owned, int Capacity, int? PurchaseCost);

/// <summary>All capacity-bounded 40-card quantity vectors; no strategic shortlist or type exclusions.</summary>
public sealed class DeckQuantitySpace
{
    private readonly DeckQuantityCapacity[] _cards;
    private readonly BigInteger[,] _suffix;
    private readonly long _budget;
    private readonly DeckQuantityCapacity?[] _byId = new DeckQuantityCapacity?[723];
    public IReadOnlyList<DeckQuantityCapacity> Capacities => Array.AsReadOnly(_cards);
    public BigInteger CapacityVectorCount => _suffix[0, 40];

    public DeckQuantitySpace(FusionCatalog catalog, IEnumerable<OwnedCardQuantity> inventory, DeckOptimizationOptions options,
        bool useStarChips = false, uint starChips = 0, CancellationToken cancellationToken = default)
    {
        if (options.CopyLimit is < 1 or > 40) throw new ArgumentOutOfRangeException(nameof(options));
        _budget = useStarChips ? starChips : 0;
        var owned = new Dictionary<int, int>();
        foreach (var entry in inventory)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = catalog.GetCard(entry.CardId);
            ArgumentOutOfRangeException.ThrowIfNegative(entry.Quantity);
            owned[entry.CardId] = checked(owned.GetValueOrDefault(entry.CardId) + entry.Quantity);
        }
        var excluded = (options.AlreadyRedeemedCardNames ?? new HashSet<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _cards = catalog.Cards.OrderBy(c => c.Id).Select(card =>
        {
            var copies = owned.GetValueOrDefault(card.Id);
            var canBuy = useStarChips && !excluded.Contains(card.Name) && card.StarchipCost is > 0 and <= 999999 &&
                card.Password is { Length: 8 } password && password.All(char.IsAsciiDigit);
            var limit = OwnedDeckOptimizer.LegalCopyLimitForCard(card.Id, options.CopyLimit);
            return new DeckQuantityCapacity(card.Id, copies, (int)Math.Min(limit, (long)copies + (canBuy ? 1 : 0)),
                canBuy ? card.StarchipCost : null);
        }).Where(c => c.Capacity > 0).ToArray();
        foreach (var card in _cards) _byId[card.CardId] = card;
        _suffix = new BigInteger[_cards.Length + 1, 41];
        _suffix[_cards.Length, 0] = BigInteger.One;
        for (var i = _cards.Length - 1; i >= 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var remaining = 0; remaining <= 40; remaining++)
                for (var quantity = 0; quantity <= Math.Min(remaining, _cards[i].Capacity); quantity++)
                    _suffix[i, remaining] += _suffix[i + 1, remaining - quantity];
        }
    }

    /// <summary>Budget-invalid prefixes skip exactly their capacity-bounded descendant count.</summary>
    public DeckQuantityCandidate Resolve(BigInteger ordinal, CancellationToken cancellationToken = default)
    {
        if (ordinal < 0 || ordinal >= CapacityVectorCount) throw new ArgumentOutOfRangeException(nameof(ordinal));
        var originalOrdinal = ordinal;
        var start = BigInteger.Zero;
        var remaining = 40;
        var deck = new List<int>(40);
        long cost = 0;
        for (var i = 0; i < _cards.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var card = _cards[i];
            for (var quantity = 0; quantity <= Math.Min(card.Capacity, remaining); quantity++)
            {
                var size = _suffix[i + 1, remaining - quantity];
                if (ordinal >= size) { ordinal -= size; start += size; continue; }
                var deficit = Math.Max(0, quantity - card.Owned);
                if (deficit > 1 || (deficit > 0 && card.PurchaseCost is null))
                    throw new InvalidDataException("Capacity permitted an ineligible purchase.");
                cost += deficit * (long)(card.PurchaseCost ?? 0);
                if (cost > _budget) return new(null, cost, start + size);
                for (var j = 0; j < quantity; j++) deck.Add(card.CardId);
                remaining -= quantity;
                break;
            }
        }
        if (remaining != 0 || ordinal != 0) throw new InvalidDataException("Quantity-space rank did not resolve to a complete deck.");
        return new(deck.ToArray(), cost, originalOrdinal + 1);
    }

    public bool IsLegal(IReadOnlyList<int> deck, out long spend)
    {
        spend = 0;
        if (deck.Count != 40) return false;
        Span<int> counts = stackalloc int[723];
        counts.Clear();
        foreach (var id in deck)
        {
            if (id is < 1 or > 722 || _byId[id] is not { } card || ++counts[id] > card.Capacity) return false;
            if (counts[id] <= card.Owned) continue;
            if (counts[id] - card.Owned > 1 || card.PurchaseCost is null) return false;
            spend += card.PurchaseCost.Value;
        }
        return spend <= _budget;
    }
}
