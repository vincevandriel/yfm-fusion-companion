using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public sealed class OwnedDeckOptimizer(FusionCatalog catalog)
{
    private const int FirstExodiaPieceId = 17;
    private const int LastExodiaPieceId = 21;
    private readonly FusionCatalog _catalog = catalog;
    private readonly DeckAnalyzer _analyzer = new(catalog);
    private readonly Dictionary<int, CardHeuristic> _allHeuristics = BuildHeuristics(catalog, includeGlitches: true);
    private readonly Dictionary<int, CardHeuristic> _intendedHeuristics = BuildHeuristics(catalog, includeGlitches: false);
    private readonly ForbiddenMemoriesStrategyEvaluator _strategyEvaluator = new(catalog);

    public DeckOptimizationReport Optimize(
        IEnumerable<OwnedCardQuantity> ownedCards,
        DeckOptimizationOptions? options = null,
        IEnumerable<int>? currentDeckCardIds = null,
        IProgress<DeckOptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DeckOptimizationOptions();
        ValidateOptions(options);
        var owned = NormalizeOwnedCards(ownedCards);
        var capacities = owned.ToDictionary(
            item => item.Key,
            item => Math.Min(item.Value, LegalCopyLimitForCard(item.Key, options.CopyLimit)));
        if (capacities.Values.Sum() < 40)
        {
            throw new ArgumentException("At least 40 owned card copies are required after applying the copy limit.", nameof(ownedCards));
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new DeckOptimizationProgress("Building deterministic candidates", 0, 1));
        var candidates = BuildCandidates(capacities, options, cancellationToken);
        progress?.Report(new DeckOptimizationProgress("Building deterministic candidates", 1, 1));

        var sampled = RankBySampledHands(candidates, options, cancellationToken, progress);
        var finalists = sampled.Take(Math.Min(options.ExactFinalists, sampled.Length)).ToArray();
        var exactResults = new List<(int[] Deck, DeckAnalysisReport Report)>(finalists.Length);
        for (var index = 0; index < finalists.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new DeckOptimizationProgress("Exact finalist analysis", index, finalists.Length));
            exactResults.Add((
                finalists[index].Deck,
                _analyzer.Analyze(finalists[index].Deck, options.IncludeGlitches, cancellationToken: cancellationToken)));
        }

        progress?.Report(new DeckOptimizationProgress("Exact finalist analysis", finalists.Length, finalists.Length));
        var (Deck, Report) = exactResults
            .OrderByDescending(item => item.Report.AtLeast2800Probability)
            .ThenByDescending(item => item.Report.AtLeast2500Probability)
            .ThenByDescending(item => item.Report.ExpectedBestFusionAttack)
            .ThenByDescending(item => item.Report.AnyFusionProbability)
            .ThenBy(item => DeckKey(item.Deck), StringComparer.Ordinal)
            .First();
        var entries = BuildEntries(Deck, options);
        var targets = BuildTargets(Report);
        var limitedCards = BuildLimitedCards(Deck, owned, capacities, options);
        var excluded = BuildExcludedCards(Deck, owned, options);
        var comparison = BuildComparison(currentDeckCardIds, Report, options, cancellationToken, progress);
        return new DeckOptimizationReport(
            entries,
            Report,
            targets,
            limitedCards,
            excluded,
            comparison,
            options.Profile,
            options.RandomSeed);
    }

    private Dictionary<int, int> NormalizeOwnedCards(IEnumerable<OwnedCardQuantity> ownedCards)
    {
        ArgumentNullException.ThrowIfNull(ownedCards);
        var owned = new Dictionary<int, int>();
        foreach (var item in ownedCards)
        {
            _ = _catalog.GetCard(item.CardId);
            if (item.Quantity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ownedCards), "Owned quantities cannot be negative.");
            }

            if (item.Quantity == 0)
            {
                continue;
            }

            owned[item.CardId] = checked(owned.GetValueOrDefault(item.CardId) + item.Quantity);
        }

        return owned;
    }

    private int[][] BuildCandidates(
        IReadOnlyDictionary<int, int> capacities,
        DeckOptimizationOptions options,
        CancellationToken cancellationToken)
    {
        var (Synergy, Strength, Flexibility) = options.Profile switch
        {
            DeckStrategyProfile.FusionConsistency => (Synergy: 1.65, Strength: 0.55, Flexibility: 0.85),
            DeckStrategyProfile.MaximumPower => (Synergy: 0.85, Strength: 1.70, Flexibility: 0.15),
            DeckStrategyProfile.ControlAndSafety => (Synergy: 0.80, Strength: 0.80, Flexibility: 0.45),
            DeckStrategyProfile.FieldAndType => (Synergy: 1.05, Strength: 1.00, Flexibility: 0.55),
            DeckStrategyProfile.RitualExperiment => (Synergy: 0.90, Strength: 1.10, Flexibility: 0.25),
            _ => (Synergy: 1.0, Strength: 1.0, Flexibility: 0.35)
        };
        var configurations = new List<CandidateConfiguration>
        {
            new(Synergy, Strength, Flexibility, null),
            new(Synergy * 1.25, Strength * 0.85, Flexibility, null),
            new(Synergy * 0.80, Strength * 1.20, Flexibility * 1.20, null),
            new(Synergy, Strength, Flexibility * 0.40, null)
        };
        var ownedIds = capacities.Keys.ToHashSet();
        var targetIds = _catalog.FusionPairs
            .Where(pair => options.IncludeGlitches || !pair.IsGlitch)
            .Where(pair => ownedIds.Contains(pair.MaterialLowId) && ownedIds.Contains(pair.MaterialHighId))
            .GroupBy(pair => pair.ResultCardId)
            .Select(group => new
            {
                ResultId = group.Key,
                Score = _catalog.GetCard(group.Key).Attack * group.Count()
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.ResultId)
            .Take(5)
            .Select(item => item.ResultId);
        configurations.AddRange(targetIds.Select(targetId => new CandidateConfiguration(1.0, 0.9, 0.15, targetId)));

        var candidates = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var configuration in configurations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var deck = BuildGreedyDeck(capacities, configuration, options);
            ImproveByLocalSearch(deck, capacities, configuration, options, cancellationToken);
            Array.Sort(deck);
            candidates.TryAdd(DeckKey(deck), deck);
        }

        return [.. candidates.Values];
    }

    private int[] BuildGreedyDeck(
        IReadOnlyDictionary<int, int> capacities,
        CandidateConfiguration configuration,
        DeckOptimizationOptions options)
    {
        var deck = new List<int>(40);
        var counts = new Dictionary<int, int>();
        while (deck.Count < 40)
        {
            var selected = capacities.Keys
                .Where(cardId => counts.GetValueOrDefault(cardId) < capacities[cardId])
                .Select(cardId => new
                {
                    CardId = cardId,
                    Score = MarginalScore(cardId, deck, counts.GetValueOrDefault(cardId), configuration, options)
                })
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.CardId)
                .First();
            deck.Add(selected.CardId);
            counts[selected.CardId] = counts.GetValueOrDefault(selected.CardId) + 1;
        }

        return [.. deck];
    }

    private void ImproveByLocalSearch(
        int[] deck,
        IReadOnlyDictionary<int, int> capacities,
        CandidateConfiguration configuration,
        DeckOptimizationOptions options,
        CancellationToken cancellationToken)
    {
        var counts = deck.GroupBy(cardId => cardId).ToDictionary(group => group.Key, group => group.Count());
        var alternatives = capacities.Keys
            .OrderByDescending(cardId => IndividualScore(cardId, configuration, options))
            .ThenBy(cardId => cardId)
            .Take(60)
            .ToArray();
        for (var pass = 0; pass < 3; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bestDelta = 0.0;
            var bestIndex = -1;
            var bestCardId = -1;
            for (var index = 0; index < deck.Length; index++)
            {
                var removed = deck[index];
                foreach (var candidate in alternatives)
                {
                    if (candidate == removed || counts.GetValueOrDefault(candidate) >= capacities[candidate])
                    {
                        continue;
                    }

                    var delta = IndividualScore(candidate, configuration, options) - IndividualScore(removed, configuration, options);
                    for (var other = 0; other < deck.Length; other++)
                    {
                        if (other == index)
                        {
                            continue;
                        }

                        delta += PairScore(candidate, deck[other], configuration, options) - PairScore(removed, deck[other], configuration, options);
                    }

                    if (delta > bestDelta + 0.0001 ||
                        (Math.Abs(delta - bestDelta) < 0.0001 && candidate < bestCardId))
                    {
                        bestDelta = delta;
                        bestIndex = index;
                        bestCardId = candidate;
                    }
                }
            }

            if (bestIndex < 0)
            {
                break;
            }

            var oldCardId = deck[bestIndex];
            counts[oldCardId]--;
            deck[bestIndex] = bestCardId;
            counts[bestCardId] = counts.GetValueOrDefault(bestCardId) + 1;
        }
    }

    private SampledCandidate[] RankBySampledHands(
        int[][] candidates,
        DeckOptimizationOptions options,
        CancellationToken cancellationToken,
        IProgress<DeckOptimizationProgress>? progress)
    {
        var sampleIndexes = BuildSampleIndexes(options.SampleHands, options.RandomSeed);
        var ranked = new List<SampledCandidate>(candidates.Length);
        for (var candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new DeckOptimizationProgress("Sampled candidate analysis", candidateIndex, candidates.Length));
            var deck = candidates[candidateIndex];
            var score = 0.0;
            var hand = new int[5];
            foreach (var indexes in sampleIndexes)
            {
                for (var index = 0; index < hand.Length; index++)
                {
                    hand[index] = deck[indexes[index]];
                }

                var report = _analyzer.Analyze(hand, options.IncludeGlitches, cancellationToken: cancellationToken);
                score += ExactScore(report);
            }

            ranked.Add(new SampledCandidate(deck, score / sampleIndexes.Length));
        }

        progress?.Report(new DeckOptimizationProgress("Sampled candidate analysis", candidates.Length, candidates.Length));
        return [.. ranked
            .OrderByDescending(item => item.Score)
            .ThenBy(item => DeckKey(item.Deck), StringComparer.Ordinal)];
    }

    private static int[][] BuildSampleIndexes(int count, int seed)
    {
        var samples = new int[count][];
        var state = unchecked((uint)seed) | 1U;
        for (var sample = 0; sample < count; sample++)
        {
            var selected = new HashSet<int>();
            while (selected.Count < 5)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                selected.Add((int)(state % 40));
            }

            samples[sample] = [.. selected.Order()];
        }

        return samples;
    }

    private OptimizedDeckEntry[] BuildEntries(IReadOnlyList<int> deck, DeckOptimizationOptions options)
    {
        var deckCounts = deck.GroupBy(cardId => cardId).ToDictionary(group => group.Key, group => group.Count());
        return [.. deckCounts
            .Select(item =>
            {
                var partners = deckCounts.Keys.Count(other =>
                    other != item.Key &&
                    (_catalog.TryResolvePair(item.Key, other, options.IncludeGlitches, out _, out _) ||
                     _catalog.ResolveEquip(item.Key, other) is not null));
                var bestResult = deckCounts.Keys
                    .Select(other => _catalog.Resolve(item.Key, other, options.IncludeGlitches))
                    .Where(result => result is not null)
                    .OrderByDescending(result => result!.Result.Attack)
                    .FirstOrDefault();
                var assessment = _strategyEvaluator.Assess(_catalog.GetCard(item.Key), options);
                var fusionReason = bestResult is null
                    ? $"{partners} compatible deck partners; base ATK {_catalog.GetCard(item.Key).Attack:N0}."
                    : $"{partners} compatible deck partners; reaches {bestResult.Result.Name} ({bestResult.Result.Attack:N0} ATK).";
                var reason = $"{assessment.Role}: {assessment.Rationale} {fusionReason}";
                return new OptimizedDeckEntry(_catalog.GetCard(item.Key), item.Value, reason);
            })
            .OrderByDescending(entry => entry.Copies)
            .ThenByDescending(entry => entry.Card.Attack)
            .ThenBy(entry => entry.Card.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static OptimizationTarget[] BuildTargets(DeckAnalysisReport report) =>
        [.. report.FusionResults
            .OrderByDescending(result => result.Probability * Math.Max(1, result.EffectiveAttack))
            .ThenByDescending(result => result.EffectiveAttack)
            .Take(10)
            .Select(result => new OptimizationTarget(
                result.Result,
                result.Probability,
                result.EffectiveAttack,
                result.IsEquipped,
                FormatRoute(result.RepresentativeRoute)))];

    private LimitedCardExplanation[] BuildLimitedCards(
        IReadOnlyList<int> deck,
        Dictionary<int, int> owned,
        Dictionary<int, int> capacities,
        DeckOptimizationOptions options)
    {
        var counts = deck.GroupBy(cardId => cardId).ToDictionary(group => group.Key, group => group.Count());
        return [.. counts
            .Where(item => item.Value == capacities[item.Key])
            .OrderByDescending(item => owned[item.Key] > capacities[item.Key])
            .ThenByDescending(item => Heuristics(options)[item.Key].Individual)
            .Select(item => new LimitedCardExplanation(
                _catalog.GetCard(item.Key),
                item.Value,
                owned[item.Key],
                owned[item.Key] > capacities[item.Key]
                    ? item.Key is >= FirstExodiaPieceId and <= LastExodiaPieceId
                        ? "Exodia piece one-copy limit"
                        : $"{options.CopyLimit}-copy deck limit"
                    : "All owned copies used"))];
    }

    public static int LegalCopyLimitForCard(int cardId, int configuredCopyLimit = 3) =>
        cardId is >= FirstExodiaPieceId and <= LastExodiaPieceId
            ? Math.Min(1, configuredCopyLimit)
            : configuredCopyLimit;

    private CardStrategyAssessment[] BuildExcludedCards(
        IReadOnlyList<int> deck,
        IReadOnlyDictionary<int, int> owned,
        DeckOptimizationOptions options)
    {
        var included = deck.ToHashSet();
        return [.. owned.Keys
            .Where(cardId => !included.Contains(cardId))
            .Select(cardId => _strategyEvaluator.Assess(_catalog.GetCard(cardId), options))
            .Where(assessment => assessment.Tier is CardViabilityTier.LowValue or CardViabilityTier.NonViable)
            .OrderBy(assessment => assessment.Tier)
            .ThenBy(assessment => assessment.Card.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private DeckComparison? BuildComparison(
        IEnumerable<int>? currentDeckCardIds,
        DeckAnalysisReport optimized,
        DeckOptimizationOptions options,
        CancellationToken cancellationToken,
        IProgress<DeckOptimizationProgress>? progress)
    {
        if (currentDeckCardIds is null)
        {
            return null;
        }

        var current = currentDeckCardIds.ToArray();
        if (current.Length == 0)
        {
            return null;
        }

        progress?.Report(new DeckOptimizationProgress("Exact current-deck comparison", 0, 1));
        var report = _analyzer.Analyze(current, options.IncludeGlitches, cancellationToken: cancellationToken);
        progress?.Report(new DeckOptimizationProgress("Exact current-deck comparison", 1, 1));
        return new DeckComparison(
            report,
            optimized.AnyFusionProbability - report.AnyFusionProbability,
            optimized.AtLeast2800Probability - report.AtLeast2800Probability,
            optimized.ExpectedBestFusionAttack - report.ExpectedBestFusionAttack);
    }

    private double MarginalScore(
        int cardId,
        IReadOnlyList<int> deck,
        int existingCopies,
        CandidateConfiguration configuration,
        DeckOptimizationOptions options)
    {
        var score = IndividualScore(cardId, configuration, options) / (1 + (existingCopies * 0.16));
        foreach (var other in deck)
        {
            score += PairScore(cardId, other, configuration, options);
        }

        return score;
    }

    private double IndividualScore(int cardId, CandidateConfiguration configuration, DeckOptimizationOptions options)
    {
        var heuristic = Heuristics(options)[cardId];
        var targetBonus = configuration.TargetResultId is null
            ? 0
            : heuristic.ResultCounts.GetValueOrDefault(configuration.TargetResultId.Value) * 1_500;
        var strategic = _strategyEvaluator.Assess(_catalog.GetCard(cardId), options).StrategicScore;
        var controlMultiplier = options.Profile == DeckStrategyProfile.ControlAndSafety ? 1.6 : 1.0;
        return (heuristic.Individual * configuration.SynergyWeight) +
               (heuristic.BaseStrength * configuration.StrengthWeight) +
               (heuristic.Flexibility * configuration.FlexibilityWeight) +
               (strategic * controlMultiplier) +
               targetBonus;
    }

    private double PairScore(
        int first,
        int second,
        CandidateConfiguration configuration,
        DeckOptimizationOptions options)
    {
        if (_catalog.TryResolvePair(first, second, options.IncludeGlitches, out var resultId, out _))
        {
            var result = _catalog.GetCard(resultId);
            var targetBonus = resultId == configuration.TargetResultId ? 2_000 : 0;
            return ((result.Attack + (result.Defense * 0.15)) * configuration.SynergyWeight) + targetBonus;
        }

        var equip = _catalog.ResolveEquip(first, second);
        if (equip is not null)
        {
            return (equip.EquippedCard.Attack + equip.AttackBonus) * configuration.SynergyWeight * 0.8;
        }

        var firstCard = _catalog.GetCard(first);
        var secondCard = _catalog.GetCard(second);
        if (ForbiddenMemoriesStrategyEvaluator.IsFieldCard(first))
        {
            return ForbiddenMemoriesStrategyEvaluator.GetFieldModifier(first, secondCard.PrimaryType) *
                   (options.Profile == DeckStrategyProfile.FieldAndType ? 6 : 2);
        }

        return ForbiddenMemoriesStrategyEvaluator.IsFieldCard(second)
            ? ForbiddenMemoriesStrategyEvaluator.GetFieldModifier(second, firstCard.PrimaryType) *
              (options.Profile == DeckStrategyProfile.FieldAndType ? 6 : 2)
            : 0;
    }

    private Dictionary<int, CardHeuristic> Heuristics(DeckOptimizationOptions options) =>
        options.IncludeGlitches ? _allHeuristics : _intendedHeuristics;

    private static Dictionary<int, CardHeuristic> BuildHeuristics(FusionCatalog catalog, bool includeGlitches)
    {
        var resultCounts = catalog.Cards.ToDictionary(card => card.Id, _ => new Dictionary<int, int>());
        var partnerCounts = catalog.Cards.ToDictionary(card => card.Id, _ => 0);
        var bestResult = catalog.Cards.ToDictionary(card => card.Id, _ => 0);
        foreach (var pair in catalog.FusionPairs.Where(pair => includeGlitches || !pair.IsGlitch))
        {
            var resultAttack = catalog.GetCard(pair.ResultCardId).Attack;
            foreach (var material in new[] { pair.MaterialLowId, pair.MaterialHighId })
            {
                partnerCounts[material]++;
                bestResult[material] = Math.Max(bestResult[material], resultAttack);
                resultCounts[material][pair.ResultCardId] = resultCounts[material].GetValueOrDefault(pair.ResultCardId) + 1;
            }
        }

        return catalog.Cards.ToDictionary(
            card => card.Id,
            card =>
            {
                var details = catalog.GetAdvancedDetails(card.Id);
                var baseStrength = Math.Max(card.Attack, card.Defense);
                var flexibility = partnerCounts[card.Id] + details.CanEquipCount + details.EquippedByCount;
                var individual = (bestResult[card.Id] * 0.55) + (flexibility * 18) + (baseStrength * 0.12);
                return new CardHeuristic(individual, baseStrength, flexibility, resultCounts[card.Id]);
            });
    }

    private static double ExactScore(DeckAnalysisReport report) =>
        (report.AtLeast2800Probability * 1_000_000_000_000) +
        (report.AtLeast2500Probability * 1_000_000_000) +
        (report.ExpectedBestFusionAttack * 1_000) +
        report.AnyFusionProbability;

    private static string FormatRoute(DeckFusionRoute route)
    {
        var text = string.Join(" + ", route.Materials.Select(card => card.Name));
        return route.EndsWithEquip ? $"{text} (final equip)" : text;
    }

    private static string DeckKey(IEnumerable<int> deck) => string.Join(',', deck.Order());

    private static void ValidateOptions(DeckOptimizationOptions options)
    {
        if (options.CopyLimit is < 1 or > 40)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Copy limit must be between 1 and 40.");
        }

        if (options.SampleHands < 1 || options.ExactFinalists < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Sampling and finalist counts must be positive.");
        }
    }

    private sealed record CandidateConfiguration(
        double SynergyWeight,
        double StrengthWeight,
        double FlexibilityWeight,
        int? TargetResultId);

    private sealed record CardHeuristic(
        double Individual,
        int BaseStrength,
        int Flexibility,
        IReadOnlyDictionary<int, int> ResultCounts);

    private sealed record SampledCandidate(int[] Deck, double Score);
}
