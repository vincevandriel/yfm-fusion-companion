using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public sealed record RecommendedCardPurchase(
    Card Card,
    int Copies,
    int UnitCost,
    long TotalCost,
    string Rationale);

public sealed record StarChipDeckPlan(
    bool StarChipsEnabled,
    uint StartingStarChips,
    uint SpentStarChips,
    uint RemainingStarChips,
    IReadOnlyList<RecommendedCardPurchase> Purchases,
    DeckOptimizationReport ResultingDeck,
    string Methodology)
{
    public const string BestFoundMethodology =
        "Best found from deterministic purchase candidates and exact finalist hand analysis; this is not a proof of global optimality. No save or game data is modified.";
}

public sealed class StarChipDeckPlanner(FusionCatalog catalog)
{
    private const int MaximumCandidatePurchases = 40;
    private readonly FusionCatalog _catalog = catalog;
    private readonly OwnedDeckOptimizer _optimizer = new(catalog);

    public StarChipDeckPlan Plan(
        IEnumerable<OwnedCardQuantity> ownedCards,
        uint starChips,
        bool useStarChips,
        DeckOptimizationOptions? options = null,
        IEnumerable<int>? currentDeckCardIds = null,
        IProgress<DeckOptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownedCards);
        options ??= new DeckOptimizationOptions();
        var owned = NormalizeOwned(ownedCards);
        if (!useStarChips)
        {
            var ownedOnlyDeck = _optimizer.Optimize(
                owned.Select(item => new OwnedCardQuantity(item.Key, item.Value)),
                options,
                currentDeckCardIds,
                progress,
                cancellationToken);
            return new StarChipDeckPlan(
                false,
                starChips,
                0,
                starChips,
                [],
                ownedOnlyDeck,
                StarChipDeckPlan.BestFoundMethodology);
        }

        var virtualOwned = new Dictionary<int, int>(owned);
        var provisionalPurchases = new Dictionary<int, int>();
        var purchasedCardNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remaining = (long)starChips;
        for (var step = 0; step < MaximumCandidatePurchases; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = _catalog.Cards
                .Where(card => IsPurchasable(card, remaining))
                .Where(card => !purchasedCardNames.Contains(card.Name))
                .Where(card => virtualOwned.GetValueOrDefault(card.Id) <
                    OwnedDeckOptimizer.LegalCopyLimitForCard(card.Id, options.CopyLimit))
                .Select(card => new
                {
                    Card = card,
                    Score = PurchaseCandidateScore(card, virtualOwned, options)
                })
                .Where(item => item.Score > 0 || LegalCapacity(virtualOwned, options.CopyLimit) < 40)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Card.StarchipCost)
                .ThenBy(item => item.Card.Id)
                .FirstOrDefault();
            if (candidate is null)
            {
                break;
            }

            var cost = candidate.Card.StarchipCost!.Value;
            virtualOwned[candidate.Card.Id] = virtualOwned.GetValueOrDefault(candidate.Card.Id) + 1;
            provisionalPurchases.Add(candidate.Card.Id, 1);
            purchasedCardNames.Add(candidate.Card.Name);
            remaining -= cost;
        }

        if (LegalCapacity(virtualOwned, options.CopyLimit) < 40)
        {
            throw new InvalidOperationException(
                "The owned collection plus affordable password purchases cannot supply 40 legal deck copies within the available Star Chips.");
        }

        var provisionalDeck = _optimizer.Optimize(
            virtualOwned.Select(item => new OwnedCardQuantity(item.Key, item.Value)),
            options,
            currentDeckCardIds,
            progress,
            cancellationToken);
        var deckCounts = provisionalDeck.Deck.ToDictionary(entry => entry.Card.Id, entry => entry.Copies);
        var requiredPurchases = provisionalPurchases
            .Select(item => new
            {
                CardId = item.Key,
                Copies = Math.Min(
                    item.Value,
                    Math.Max(0, deckCounts.GetValueOrDefault(item.Key) - owned.GetValueOrDefault(item.Key)))
            })
            .Where(item => item.Copies > 0)
            .ToDictionary(item => item.CardId, item => item.Copies);

        DeckOptimizationReport resultingDeck;
        while (true)
        {
            var finalVirtualOwned = new Dictionary<int, int>(owned);
            foreach (var purchase in requiredPurchases)
            {
                finalVirtualOwned[purchase.Key] = finalVirtualOwned.GetValueOrDefault(purchase.Key) + purchase.Value;
            }

            if (LegalCapacity(finalVirtualOwned, options.CopyLimit) < 40)
            {
                // A legal provisional deck necessarily contains enough purchased copies. This guard
                // prevents a future optimizer/report change from returning an internally inconsistent plan.
                throw new InvalidOperationException("The proposed purchase list does not support its resulting 40-card deck.");
            }

            resultingDeck = _optimizer.Optimize(
                finalVirtualOwned.Select(item => new OwnedCardQuantity(item.Key, item.Value)),
                options,
                currentDeckCardIds,
                progress,
                cancellationToken);
            var resultingCounts = resultingDeck.Deck.ToDictionary(entry => entry.Card.Id, entry => entry.Copies);
            var reducedPurchases = requiredPurchases
                .Select(item => new
                {
                    CardId = item.Key,
                    Copies = Math.Min(
                        item.Value,
                        Math.Max(0, resultingCounts.GetValueOrDefault(item.Key) - owned.GetValueOrDefault(item.Key)))
                })
                .Where(item => item.Copies > 0)
                .ToDictionary(item => item.CardId, item => item.Copies);
            if (requiredPurchases.Count == reducedPurchases.Count &&
                requiredPurchases.All(item => reducedPurchases.GetValueOrDefault(item.Key) == item.Value))
            {
                break;
            }

            requiredPurchases = reducedPurchases;
        }
        var purchases = requiredPurchases
            .Select(item =>
            {
                var card = _catalog.GetCard(item.Key);
                var unitCost = card.StarchipCost!.Value;
                return new RecommendedCardPurchase(
                    card,
                    item.Value,
                    unitCost,
                    checked((long)unitCost * item.Value),
                    "Adds a legal copy used by the resulting optimized deck; purchase order follows marginal fusion and strength value.");
            })
            .OrderByDescending(item => PurchaseCandidateScore(item.Card, owned, options))
            .ThenBy(item => item.Card.Id)
            .ToArray();
        var spent = checked((uint)purchases.Sum(item => item.TotalCost));
        return new StarChipDeckPlan(
            true,
            starChips,
            spent,
            starChips - spent,
            purchases,
            resultingDeck,
            StarChipDeckPlan.BestFoundMethodology);
    }

    private Dictionary<int, int> NormalizeOwned(IEnumerable<OwnedCardQuantity> ownedCards)
    {
        var normalized = new Dictionary<int, int>();
        foreach (var item in ownedCards)
        {
            _ = _catalog.GetCard(item.CardId);
            if (item.Quantity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ownedCards), "Owned quantities cannot be negative.");
            }

            normalized[item.CardId] = checked(normalized.GetValueOrDefault(item.CardId) + item.Quantity);
        }

        return normalized;
    }

    private static bool IsPurchasable(Card card, long remainingStarChips) =>
        card.Password is { Length: 8 } &&
        card.StarchipCost is > 0 and <= 999999 &&
        card.StarchipCost.Value <= remainingStarChips;

    private double PurchaseCandidateScore(
        Card card,
        IReadOnlyDictionary<int, int> virtualOwned,
        DeckOptimizationOptions options)
    {
        var score = card.Attack + (card.Defense * 0.15);
        foreach (var partner in virtualOwned)
        {
            if (partner.Value <= 0)
            {
                continue;
            }

            if (_catalog.TryResolvePair(card.Id, partner.Key, options.IncludeGlitches, out var resultId, out _))
            {
                score += _catalog.GetCard(resultId).Attack * Math.Min(partner.Value, 3) * 0.45;
            }
            else if (_catalog.ResolveEquip(card.Id, partner.Key) is not null)
            {
                score += 250;
            }
        }

        score -= Math.Log10(Math.Max(1, card.StarchipCost!.Value)) * 125;
        return score;
    }

    private static int LegalCapacity(IReadOnlyDictionary<int, int> owned, int copyLimit) =>
        owned.Sum(item => Math.Min(item.Value, OwnedDeckOptimizer.LegalCopyLimitForCard(item.Key, copyLimit)));
}
