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
        "Best found from legal complete purchase bundles and exact finalist hand analysis; this is not a proof of global optimality. Password redemption history is not available from a save, so cards marked already redeemed are excluded. No save or game data is modified.";
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
        var ownedCapacity = LegalCapacity(owned, options.CopyLimit);
        var baseline = ownedCapacity >= 40
            ? _optimizer.Optimize(ToOwnedCards(owned), options, currentDeckCardIds, progress, cancellationToken)
            : null;
        if (!useStarChips)
        {
            return baseline is not null
                ? NoSpendPlan(starChips, baseline)
                : throw new ArgumentException("At least 40 owned card copies are required after applying the copy limit.", nameof(ownedCards));
        }

        var eligible = EligiblePurchases(options).ToArray();
        var virtualOwned = new Dictionary<int, int>(owned);
        var purchased = new Dictionary<int, int>();
        var remaining = (long)starChips;

        // First make a legal deck possible. Cheapest eligible distinct names are selected
        // before optional strength upgrades so an expensive card cannot strand the plan.
        while (LegalCapacity(virtualOwned, options.CopyLimit) < 40)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = eligible
                .Where(card => !purchased.ContainsKey(card.Id))
                .Where(card => card.StarchipCost!.Value <= remaining)
                .Where(card => virtualOwned.GetValueOrDefault(card.Id) <
                    OwnedDeckOptimizer.LegalCopyLimitForCard(card.Id, options.CopyLimit))
                .OrderBy(card => card.StarchipCost!.Value)
                .ThenBy(card => card.Id)
                .FirstOrDefault();
            candidate = candidate ?? throw new InvalidOperationException(
                "The owned collection plus eligible affordable password purchases cannot supply 40 legal deck copies within the available Star Chips.");

            AddPurchase(candidate, virtualOwned, purchased, ref remaining);
        }

        // A legal owned collection always has a zero-spend incumbent. Any virtual
        // purchase must improve the complete resulting deck, not merely a heuristic.
        var feasibilityPurchases = new Dictionary<int, int>(purchased);
        var purchasedNames = purchased.Keys.Select(cardId => _catalog.GetCard(cardId).Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var step = purchased.Count; step < MaximumCandidatePurchases; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = eligible
                .Where(card => !purchasedNames.Contains(card.Name))
                .Where(card => card.StarchipCost!.Value <= remaining)
                .Where(card => virtualOwned.GetValueOrDefault(card.Id) <
                    OwnedDeckOptimizer.LegalCopyLimitForCard(card.Id, options.CopyLimit))
                .Select(card => new { Card = card, Score = PurchaseCandidateScore(card, virtualOwned, options) })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Card.StarchipCost)
                .ThenBy(item => item.Card.Id)
                .FirstOrDefault();
            if (candidate is null)
            {
                break;
            }

            AddPurchase(candidate.Card, virtualOwned, purchased, ref remaining);
            purchasedNames.Add(candidate.Card.Name);
        }

        var candidateReport = _optimizer.Optimize(
            ToOwnedCards(virtualOwned), options, currentDeckCardIds, progress, cancellationToken);
        var candidatePurchases = RequiredPurchasesForDeck(purchased, candidateReport, owned);
        var candidateDeck = OptimizeWithPurchases(owned, candidatePurchases, options, currentDeckCardIds, progress, cancellationToken);
        candidatePurchases = RequiredPurchasesForDeck(candidatePurchases, candidateDeck, owned);
        candidateDeck = OptimizeWithPurchases(owned, candidatePurchases, options, currentDeckCardIds, progress, cancellationToken);

        if (baseline is not null && !IsStrictlyBetter(candidateDeck, baseline))
        {
            return NoSpendPlan(starChips, baseline);
        }

        // For an incomplete collection, retain optional upgrades only when the
        // complete deck improves over the lower-spend feasible purchase bundle.
        if (feasibilityPurchases.Count > 0)
        {
            var feasibilityDeck = OptimizeWithPurchases(owned, feasibilityPurchases, options, currentDeckCardIds, progress, cancellationToken);
            if (!IsStrictlyBetter(candidateDeck, feasibilityDeck))
            {
                candidatePurchases = RequiredPurchasesForDeck(feasibilityPurchases, feasibilityDeck, owned);
                candidateDeck = OptimizeWithPurchases(owned, candidatePurchases, options, currentDeckCardIds, progress, cancellationToken);
            }
        }

        var resultPurchases = ToRecommendedPurchases(candidatePurchases, owned, options);
        var spent = checked((uint)resultPurchases.Sum(item => item.TotalCost));
        return new StarChipDeckPlan(
            true,
            starChips,
            spent,
            starChips - spent,
            resultPurchases,
            candidateDeck,
            StarChipDeckPlan.BestFoundMethodology);
    }

    private static StarChipDeckPlan NoSpendPlan(uint starChips, DeckOptimizationReport deck) => new(
        false,
        starChips,
        0,
        starChips,
        [],
        deck,
        StarChipDeckPlan.BestFoundMethodology);

    private IEnumerable<Card> EligiblePurchases(DeckOptimizationOptions options)
    {
        var redeemed = options.AlreadyRedeemedCardNames ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return _catalog.Cards
            .Where(card => IsPurchasable(card, long.MaxValue))
            .Where(card => !redeemed.Contains(card.Name))
            .GroupBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(card => card.Id).First());
    }

    private static void AddPurchase(
        Card card,
        Dictionary<int, int> virtualOwned,
        Dictionary<int, int> purchased,
        ref long remaining)
    {
        var cost = card.StarchipCost!.Value;
        virtualOwned[card.Id] = virtualOwned.TryGetValue(card.Id, out var ownedCopies) ? ownedCopies + 1 : 1;
        purchased[card.Id] = purchased.TryGetValue(card.Id, out var purchasedCopies) ? purchasedCopies + 1 : 1;
        remaining -= cost;
    }

    private DeckOptimizationReport OptimizeWithPurchases(
        IReadOnlyDictionary<int, int> owned,
        IReadOnlyDictionary<int, int> purchases,
        DeckOptimizationOptions options,
        IEnumerable<int>? currentDeckCardIds,
        IProgress<DeckOptimizationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var virtualOwned = new Dictionary<int, int>(owned);
        foreach (var purchase in purchases)
        {
            virtualOwned[purchase.Key] = virtualOwned.GetValueOrDefault(purchase.Key) + purchase.Value;
        }

        if (LegalCapacity(virtualOwned, options.CopyLimit) < 40)
        {
            throw new InvalidOperationException("The proposed purchase list does not support its resulting 40-card deck.");
        }

        return _optimizer.Optimize(ToOwnedCards(virtualOwned), options, currentDeckCardIds, progress, cancellationToken);
    }

    private static Dictionary<int, int> RequiredPurchasesForDeck(
        IReadOnlyDictionary<int, int> proposed,
        DeckOptimizationReport deck,
        IReadOnlyDictionary<int, int> owned)
    {
        var deckCounts = deck.Deck.ToDictionary(entry => entry.Card.Id, entry => entry.Copies);
        return proposed
            .Select(item => new
            {
                item.Key,
                Copies = Math.Min(item.Value, Math.Max(0,
                    deckCounts.GetValueOrDefault(item.Key) - owned.GetValueOrDefault(item.Key)))
            })
            .Where(item => item.Copies > 0)
            .ToDictionary(item => item.Key, item => item.Copies);
    }

    private RecommendedCardPurchase[] ToRecommendedPurchases(
        IReadOnlyDictionary<int, int> purchases,
        IReadOnlyDictionary<int, int> owned,
        DeckOptimizationOptions options) => [.. purchases
        .Select(item =>
        {
            var card = _catalog.GetCard(item.Key);
            var unitCost = card.StarchipCost!.Value;
            return new RecommendedCardPurchase(
                card,
                item.Value,
                unitCost,
                checked((long)unitCost * item.Value),
                "Adds a legal copy used by the resulting deck after comparison with the lower-spend incumbent.");
        })
        .OrderByDescending(item => PurchaseCandidateScore(item.Card, owned, options))
        .ThenBy(item => item.Card.Id)];

    private static bool IsStrictlyBetter(DeckOptimizationReport candidate, DeckOptimizationReport incumbent)
    {
        if (candidate.SafetyAssessment is not null && incumbent.SafetyAssessment is not null)
        {
            var safeOpponentDifference = candidate.SafetyAssessment.SafeOpponentCount - incumbent.SafetyAssessment.SafeOpponentCount;
            if (safeOpponentDifference != 0)
            {
                return safeOpponentDifference > 0;
            }

            var worstOpponentDifference = candidate.SafetyAssessment.WorstOpponentScore - incumbent.SafetyAssessment.WorstOpponentScore;
            if (Math.Abs(worstOpponentDifference) > 0.0001)
            {
                return worstOpponentDifference > 0;
            }

            var coverageDifference = candidate.SafetyAssessment.EstimatedOpeningAnswerCoverage - incumbent.SafetyAssessment.EstimatedOpeningAnswerCoverage;
            if (Math.Abs(coverageDifference) > 0.0001)
            {
                return coverageDifference > 0;
            }

            var safetyDifference = candidate.SafetyAssessment.HeuristicScore - incumbent.SafetyAssessment.HeuristicScore;
            if (Math.Abs(safetyDifference) > 0.0001)
            {
                return safetyDifference > 0;
            }
        }

        var powerDifference = candidate.ExactAnalysis.AtLeast2800Probability - incumbent.ExactAnalysis.AtLeast2800Probability;
        if (Math.Abs(powerDifference) > 0.0000001) return powerDifference > 0;
        var strongDifference = candidate.ExactAnalysis.AtLeast2500Probability - incumbent.ExactAnalysis.AtLeast2500Probability;
        if (Math.Abs(strongDifference) > 0.0000001) return strongDifference > 0;
        var expectedDifference = candidate.ExactAnalysis.ExpectedBestFusionAttack - incumbent.ExactAnalysis.ExpectedBestFusionAttack;
        if (Math.Abs(expectedDifference) > 0.0001) return expectedDifference > 0;
        if (candidate.SecondarySafetyAssessment is not null && incumbent.SecondarySafetyAssessment is not null)
        {
            var secondaryDifference = candidate.SecondarySafetyAssessment.HeuristicScore - incumbent.SecondarySafetyAssessment.HeuristicScore;
            if (Math.Abs(secondaryDifference) > 0.0001)
            {
                return secondaryDifference > 0;
            }
        }

        return false;
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
        card.Password is { Length: 8 } password &&
        password.All(char.IsAsciiDigit) &&
        card.StarchipCost is > 0 and <= 999999 &&
        card.StarchipCost.Value <= remainingStarChips;

    private double PurchaseCandidateScore(
        Card card,
        IReadOnlyDictionary<int, int> virtualOwned,
        DeckOptimizationOptions options)
    {
        var score = card.Attack + (card.Defense * 0.15) +
                    OpponentSafetyScoring.CounterValue(card, options.SafetyContext) +
                    (OpponentSafetyScoring.CounterValue(card, options.SecondarySafetyContext) * 0.15);
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

    private static IEnumerable<OwnedCardQuantity> ToOwnedCards(IReadOnlyDictionary<int, int> cards) =>
        cards.Select(item => new OwnedCardQuantity(item.Key, item.Value));

    private static int LegalCapacity(IReadOnlyDictionary<int, int> owned, int copyLimit) =>
        owned.Sum(item => Math.Min(item.Value, OwnedDeckOptimizer.LegalCopyLimitForCard(item.Key, copyLimit)));
}
