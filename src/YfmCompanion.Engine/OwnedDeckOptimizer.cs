using System.Runtime.CompilerServices;
using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public sealed class OwnedDeckOptimizer
{
    private const int FirstExodiaPieceId = 17;
    private const int LastExodiaPieceId = 21;
    private static readonly ConditionalWeakTable<FusionCatalog, PreparedHeuristics> SharedHeuristics = new();
    private static readonly object HeuristicsLock = new();
    private readonly FusionCatalog _catalog;
    // One combined 256 MiB retained-search-cache budget, including prepared scores.
    private readonly DeckAnalyzer _analyzer;
    private readonly BoundedAnalysisCache _assessmentCache = new(32L * 1024 * 1024);
    private PreparedHeuristics? _heuristics;
    private readonly ForbiddenMemoriesStrategyEvaluator _strategyEvaluator;
    private DeckOptimizationOptions? _assessmentOptions;
    private readonly Dictionary<OpponentSafetyContext, string> _contextKeys = new(ReferenceEqualityComparer.Instance);
    private CancellationToken _assessmentToken;

    public OwnedDeckOptimizer(FusionCatalog catalog)
    {
        _catalog = catalog;
        _analyzer = new(catalog, 224L * 1024 * 1024);
        _strategyEvaluator = new(catalog);
    }

    private PreparedHeuristics PrepareHeuristics()
    {
        if (_heuristics is not null) return _heuristics;
        _assessmentToken.ThrowIfCancellationRequested();
        var catalog = _catalog;
        if (!SharedHeuristics.TryGetValue(catalog, out var prepared))
        {
            var computed = new PreparedHeuristics(BuildHeuristics(catalog, true, _assessmentToken),
                BuildHeuristics(catalog, false, _assessmentToken));
            lock (HeuristicsLock)
            {
                if (!SharedHeuristics.TryGetValue(catalog, out prepared))
                {
                    prepared = computed;
                    SharedHeuristics.Add(catalog, prepared);
                }
            }
        }
        return _heuristics = prepared;
    }

    public AnalysisCacheDiagnostics CacheDiagnostics
    {
        get
        {
            var hands = _analyzer.CacheDiagnostics;
            var scores = _assessmentCache.Diagnostics;
            return new(hands.AccountedBytes + scores.AccountedBytes, hands.LimitBytes + scores.LimitBytes,
                hands.Hits + scores.Hits, hands.Misses + scores.Misses, hands.Entries + scores.Entries);
        }
    }

    public int[] CreateSeedDeck(IEnumerable<OwnedCardQuantity> ownedCards, DeckOptimizationOptions options,
        CancellationToken cancellationToken = default, IProgress<DeckOptimizationProgress>? progress = null)
    {
        ValidateOptions(options);
        PrepareAssessments(options, cancellationToken);
        var capacities = NormalizeOwnedCards(ownedCards).ToDictionary(p => p.Key,
            p => Math.Min(p.Value, LegalCopyLimitForCard(p.Key, options.CopyLimit)));
        if (capacities.Values.Sum() < 40) throw new ArgumentException("At least 40 legal copies are required.", nameof(ownedCards));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        return BuildGreedyDeck(capacities, new(1, 1, .35, null), options, cancellationToken, () =>
        {
            if (clock.ElapsedMilliseconds < 200) return;
            progress?.Report(new("Preparing first legal deck", 0, 1));
            clock.Restart();
        });
    }

    private void PrepareAssessments(DeckOptimizationOptions options, CancellationToken token)
    {
        _assessmentToken = token;
        if (!ReferenceEquals(options, _assessmentOptions))
        {
            _assessmentCache.Clear();
            _contextKeys.Clear();
            _assessmentOptions = options;
        }
    }

    private PreparedAssessment Assessment(int cardId, DeckOptimizationOptions options)
    {
        if (!ReferenceEquals(options, _assessmentOptions)) PrepareAssessments(options, _assessmentToken);
        var key = new AnalysisCacheKey((ulong)cardId, "assessment");
        if (_assessmentCache.TryGet<PreparedAssessment>(key, out var cached)) return cached!;
        var card = _catalog.GetCard(cardId);
        var value = new PreparedAssessment(_strategyEvaluator.Assess(card, options),
            WeightedCounter(card, options.SafetyContext), WeightedCounter(card, options.SecondarySafetyContext));
        _assessmentCache.Add(key, value, 512 + 2L * (value.Strategy.Role.Length + value.Strategy.Rationale.Length));
        return value;
    }

    private (double Strategy, double Safety, double Secondary) Assess(int cardId, DeckOptimizationOptions options)
    {
        var value = Assessment(cardId, options);
        return (value.Strategy.StrategicScore, value.Safety, value.Secondary);
    }

    internal (double Safety, double Secondary) PurchaseCounters(int cardId, DeckOptimizationOptions options, CancellationToken token)
    {
        PrepareAssessments(options, token);
        var value = Assessment(cardId, options);
        return (value.Safety, value.Secondary);
    }

    private double[] CounterValues(Card card, OpponentSafetyContext context)
    {
        _assessmentToken.ThrowIfCancellationRequested();
        if (!_contextKeys.TryGetValue(context, out var contextKey))
        {
            contextKey = $"opponent:{_contextKeys.Count}";
            _contextKeys.Add(context, contextKey);
        }
        var key = new AnalysisCacheKey((uint)card.Id | ((ulong)(uint)card.Attack << 10), contextKey);
        if (_assessmentCache.TryGet<double[]>(key, out var cached)) return cached!;
        var values = new double[context.Threats.Count];
        for (var i = 0; i < values.Length; i++)
        {
            _assessmentToken.ThrowIfCancellationRequested();
            values[i] = OpponentSafetyScoring.CounterValueForTarget(card, context.Threats[i], context.ActiveFieldCardId);
        }
        _assessmentCache.Add(key, values, 32 + 8L * values.Length);
        return values;
    }

    private double WeightedCounter(Card card, OpponentSafetyContext? context)
    {
        if (context is null || context.Threats.Count == 0) return 0;
        var values = CounterValues(card, context);
        double total = 0, weighted = 0;
        for (var i = 0; i < values.Length; i++)
        {
            var importance = Math.Max(0, context.Threats[i].Importance);
            total += importance;
            weighted += values[i] * importance;
        }
        return total <= 0 ? 0 : weighted / total;
    }

    public DeckOptimizationReport Optimize(
        IEnumerable<OwnedCardQuantity> ownedCards,
        DeckOptimizationOptions? options = null,
        IEnumerable<int>? currentDeckCardIds = null,
        IProgress<DeckOptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DeckOptimizationOptions();
        ValidateOptions(options);
        PrepareAssessments(options, cancellationToken);
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
        var candidates = BuildCandidates(capacities, options, cancellationToken, progress);
        progress?.Report(new DeckOptimizationProgress("Building deterministic candidates", 1, 1));

        var sampled = RankBySampledHands(candidates, options, cancellationToken, progress);
        var finalists = sampled.Take(Math.Min(options.ExactFinalists, sampled.Length)).ToArray();
        var exactResults = new List<ExactCandidate>(finalists.Length);
        for (var index = 0; index < finalists.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new DeckOptimizationProgress("Exact finalist analysis", index, finalists.Length));
            var report = _analyzer.Analyze(
                finalists[index].Deck,
                options.IncludeGlitches,
                ForwardHands(progress, "Exact finalist analysis", index, finalists.Length),
                cancellationToken: cancellationToken);
            exactResults.Add(new ExactCandidate(
                finalists[index].Deck,
                report,
                BuildSafetyAssessment(finalists[index].Deck, report, options.SafetyContext),
                BuildSafetyAssessment(finalists[index].Deck, report, options.SecondarySafetyContext)));
        }

        progress?.Report(new DeckOptimizationProgress("Exact finalist analysis", finalists.Length, finalists.Length));
        var winner = exactResults.Aggregate((best, candidate) =>
            IsBetterExactCandidate(candidate, best, options) ? candidate : best);
        var Deck = winner.Deck;
        var Report = winner.Report;
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
            options.RandomSeed,
            winner.Safety,
            winner.SecondarySafety);
    }

    public DeckOptimizationReport EvaluateDeck(IEnumerable<int> cardIds, IEnumerable<OwnedCardQuantity> ownedCards,
        DeckOptimizationOptions options, bool exact = true, IProgress<DeckOptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        PrepareAssessments(options, cancellationToken);
        var owned = NormalizeOwnedCards(ownedCards);
        var deck = cardIds.Order().ToArray();
        if (deck.Length != 40 || deck.GroupBy(id => id).Any(g => g.Count() > Math.Min(owned.GetValueOrDefault(g.Key), LegalCopyLimitForCard(g.Key, options.CopyLimit))))
            throw new ArgumentException("The candidate must contain 40 legally available copies.", nameof(cardIds));
        return DescribeCandidate(EvaluateCandidate(deck, options, exact, progress, cancellationToken), owned, options);
    }

    // Internal callers have already validated the deck through DeckQuantitySpace.
    // A rejected search candidate needs scores, not 722-card ownership copies or prose.
    internal DeckOptimizationReport EvaluateCandidate(int[] deck, DeckOptimizationOptions options, bool exact,
        IProgress<DeckOptimizationProgress>? progress, CancellationToken cancellationToken)
    {
        ValidateOptions(options);
        PrepareAssessments(options, cancellationToken);
        var report = exact
            ? _analyzer.Analyze(deck, options.IncludeGlitches, ForwardHands(progress, "Exact deck analysis", 0, 1), cancellationToken)
            : _analyzer.AnalyzeSampled(deck, options.SampleHands, options.RandomSeed, options.IncludeGlitches,
                ForwardHands(progress, "Sampled deck analysis", 0, 1), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var entries = deck.GroupBy(id => id).Select(g => new OptimizedDeckEntry(_catalog.GetCard(g.Key), g.Count(), "")).ToArray();
        return new(entries, report, [], [], [], null, options.Profile, options.RandomSeed,
            BuildSafetyAssessment(deck, report, options.SafetyContext), BuildSafetyAssessment(deck, report, options.SecondarySafetyContext));
    }

    internal DeckOptimizationReport DescribeCandidate(DeckOptimizationReport report,
        IEnumerable<OwnedCardQuantity> ownedCards, DeckOptimizationOptions options) =>
        DescribeCandidate(report, NormalizeOwnedCards(ownedCards), options);

    private DeckOptimizationReport DescribeCandidate(DeckOptimizationReport report,
        Dictionary<int, int> owned, DeckOptimizationOptions options)
    {
        var deck = report.Deck.SelectMany(e => Enumerable.Repeat(e.Card.Id, e.Copies)).ToArray();
        var capacities = owned.ToDictionary(p => p.Key, p => Math.Min(p.Value, LegalCopyLimitForCard(p.Key, options.CopyLimit)));
        return report with
        {
            Deck = BuildEntries(deck, options),
            ImportantTargets = BuildTargets(report.ExactAnalysis),
            LimitedCards = BuildLimitedCards(deck, owned, capacities, options),
            ExcludedOrLowValueCards = BuildExcludedCards(deck, owned, options)
        };
    }

    private static IProgress<DeckAnalysisProgress> ForwardHands(IProgress<DeckOptimizationProgress>? progress,
        string stage, int index, int total) => new InlineProgress<DeckAnalysisProgress>(p =>
            progress?.Report(new(stage, index, total) { CompletedHands = p.CompletedHands, TotalHands = p.TotalHands }));

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
        CancellationToken cancellationToken,
        IProgress<DeckOptimizationProgress>? progress)
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
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var completed = 0;
        void Activity()
        {
            if (watch.ElapsedMilliseconds < 200) return;
            progress?.Report(new("Building deterministic candidates", completed, configurations.Count));
            watch.Restart();
        }
        foreach (var configuration in configurations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var deck = BuildGreedyDeck(capacities, configuration, options, cancellationToken, Activity);
            ImproveByLocalSearch(deck, capacities, configuration, options, cancellationToken, Activity);
            Array.Sort(deck);
            candidates.TryAdd(DeckKey(deck), deck);
            completed++;
        }

        return [.. candidates.Values];
    }

    private int[] BuildGreedyDeck(
        IReadOnlyDictionary<int, int> capacities,
        CandidateConfiguration configuration,
        DeckOptimizationOptions options,
        CancellationToken cancellationToken,
        Action? activity = null)
    {
        var deck = new List<int>(40);
        var counts = new Dictionary<int, int>();
        while (deck.Count < 40)
        {
            cancellationToken.ThrowIfCancellationRequested();
            activity?.Invoke();
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
        CancellationToken cancellationToken,
        Action? activity = null)
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
                    cancellationToken.ThrowIfCancellationRequested();
                    activity?.Invoke();
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
        var ranked = new List<SampledCandidate>(candidates.Length);
        for (var candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new DeckOptimizationProgress("Sampled candidate analysis", candidateIndex, candidates.Length));
            var deck = candidates[candidateIndex];
            var report = _analyzer.AnalyzeSampled(deck, options.SampleHands, options.RandomSeed, options.IncludeGlitches,
                ForwardHands(progress, "Sampled candidate analysis", candidateIndex, candidates.Length), cancellationToken);

            ranked.Add(new SampledCandidate(
                deck,
                new DeckObjective(report, BuildSafetyAssessment(deck, report, options.SafetyContext),
                    BuildSafetyAssessment(deck, report, options.SecondarySafetyContext), deck)));
        }

        progress?.Report(new DeckOptimizationProgress("Sampled candidate analysis", candidates.Length, candidates.Length));
        return [.. ranked
            .OrderByDescending(item => item.Objective,
                new DeckObjectiveComparer(options.SafetyContext is not null, options.SecondarySafetyContext is not null))];
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
                var assessment = Assessment(item.Key, options).Strategy;
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
            .Select(cardId => Assessment(cardId, options).Strategy)
            .Where(assessment => assessment.Tier is CardViabilityTier.LowValue or CardViabilityTier.NonViable)
            .OrderBy(assessment => assessment.Tier)
            .ThenBy(assessment => assessment.Card.Name, StringComparer.OrdinalIgnoreCase)];
    }

    internal DeckOptimizationReport WithComparison(DeckOptimizationReport report, IEnumerable<int>? currentDeck,
        DeckOptimizationOptions options, IProgress<DeckOptimizationProgress>? progress, CancellationToken cancellationToken) =>
        report with { Comparison = BuildComparison(currentDeck, report.ExactAnalysis, options, cancellationToken, progress) };

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
        var report = _analyzer.Analyze(current, options.IncludeGlitches,
            ForwardHands(progress, "Exact current-deck comparison", 0, 1), cancellationToken);
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
        var (strategic, safety, secondarySafety) = Assess(cardId, options);
        var controlMultiplier = options.Profile == DeckStrategyProfile.ControlAndSafety ? 1.6 : 1.0;
        return (heuristic.Individual * configuration.SynergyWeight) +
               (heuristic.BaseStrength * configuration.StrengthWeight) +
               (heuristic.Flexibility * configuration.FlexibilityWeight) +
               (strategic * controlMultiplier) +
               (safety * controlMultiplier) +
               (secondarySafety * 0.15) +
               targetBonus;
    }

    private double PairScore(
        int first,
        int second,
        CandidateConfiguration configuration,
        DeckOptimizationOptions options)
    {
        var key = new AnalysisCacheKey((uint)Math.Min(first, second) | ((ulong)(uint)Math.Max(first, second) << 10), "pair");
        if (!_assessmentCache.TryGet<PreparedPair>(key, out var prepared))
        {
            prepared = PreparePair(first, second, options);
            _assessmentCache.Add(key, prepared, 64);
        }
        return prepared!.Synergy * configuration.SynergyWeight + prepared.Fixed +
               (prepared.ResultId != 0 && prepared.ResultId == configuration.TargetResultId ? 2000 : 0);
    }

    private PreparedPair PreparePair(int first, int second, DeckOptimizationOptions options)
    {
        _assessmentToken.ThrowIfCancellationRequested();
        if (_catalog.TryResolvePair(first, second, options.IncludeGlitches, out var resultId, out _))
        {
            var result = _catalog.GetCard(resultId);
            var (_, safety, secondarySafety) = Assess(resultId, options);
            return new(result.Attack + (result.Defense * 0.15),
                (safety * (options.Profile == DeckStrategyProfile.ControlAndSafety ? 1.6 : 1.0)) + (secondarySafety * 0.15), resultId);
        }

        var equip = _catalog.ResolveEquip(first, second);
        if (equip is not null)
        {
            return new((equip.EquippedCard.Attack + equip.AttackBonus) * 0.8, 0, 0);
        }

        var firstCard = _catalog.GetCard(first);
        var secondCard = _catalog.GetCard(second);
        if (ForbiddenMemoriesStrategyEvaluator.IsFieldCard(first))
        {
            return new(0, ForbiddenMemoriesStrategyEvaluator.GetFieldModifier(first, secondCard.PrimaryType) *
                   (options.Profile == DeckStrategyProfile.FieldAndType ? 6 : 2), 0);
        }

        return new(0, ForbiddenMemoriesStrategyEvaluator.IsFieldCard(second)
            ? ForbiddenMemoriesStrategyEvaluator.GetFieldModifier(second, firstCard.PrimaryType) *
              (options.Profile == DeckStrategyProfile.FieldAndType ? 6 : 2)
            : 0, 0);
    }

    private Dictionary<int, CardHeuristic> Heuristics(DeckOptimizationOptions options) =>
        options.IncludeGlitches ? PrepareHeuristics().All : PrepareHeuristics().Intended;

    private static Dictionary<int, CardHeuristic> BuildHeuristics(FusionCatalog catalog, bool includeGlitches, CancellationToken token)
    {
        var resultCounts = catalog.Cards.ToDictionary(card => card.Id, _ => new Dictionary<int, int>());
        var partnerCounts = catalog.Cards.ToDictionary(card => card.Id, _ => 0);
        var bestResult = catalog.Cards.ToDictionary(card => card.Id, _ => 0);
        foreach (var pair in catalog.FusionPairs.Where(pair => includeGlitches || !pair.IsGlitch))
        {
            token.ThrowIfCancellationRequested();
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
                token.ThrowIfCancellationRequested();
                var details = catalog.GetAdvancedDetails(card.Id);
                var baseStrength = Math.Max(card.Attack, card.Defense);
                var flexibility = partnerCounts[card.Id] + details.CanEquipCount + details.EquippedByCount;
                var individual = (bestResult[card.Id] * 0.55) + (flexibility * 18) + (baseStrength * 0.12);
                return new CardHeuristic(individual, baseStrength, flexibility, resultCounts[card.Id]);
            });
    }

    private DeckSafetyAssessment? BuildSafetyAssessment(
        int[] deck,
        DeckAnalysisReport report,
        OpponentSafetyContext? context)
    {
        if (context is null || context.Threats.Count == 0)
        {
            return null;
        }

        var deckCards = deck.Distinct().Select(id => (Id: id, Values: CounterValues(_catalog.GetCard(id), context))).ToArray();
        var fusionCounters = report.FusionResults.Select(result => (result.Probability,
            Values: CounterValues(result.Result with { Attack = result.EffectiveAttack }, context))).ToArray();
        var opponentScores = new List<double>();
        var safeOpponents = 0;
        var openingCoverages = new List<double>();
        foreach (var opponent in context.Threats.Select((target, index) => (target.OpponentId, Index: index)).GroupBy(target => target.OpponentId))
        {
            var targetScores = new List<double>();
            var answerCardIds = new HashSet<int>();
            foreach (var target in opponent)
            {
                _assessmentToken.ThrowIfCancellationRequested();
                var standalone = deckCards.Max(card => card.Values[target.Index]);
                foreach (var card in deckCards.Where(card => card.Values[target.Index] > 0))
                {
                    answerCardIds.Add(card.Id);
                }

                var fusionValue = fusionCounters
                    .Where(result => result.Values[target.Index] > 0)
                    .Sum(result => result.Probability * 650);
                targetScores.Add(standalone + fusionValue);
            }

            var opponentScore = targetScores.Average();
            opponentScores.Add(opponentScore);
            if (targetScores.All(score => score > 0))
            {
                safeOpponents++;
            }

            var answerCopies = deck.Count(cardId => answerCardIds.Contains(cardId));
            openingCoverages.Add(OpeningHandCoverage(answerCopies, deck.Length));
        }

        var heuristicScore = opponentScores.Average();
        var worstOpponentScore = opponentScores.Min();
        var openingCoverage = openingCoverages.Average();
        return new DeckSafetyAssessment(
            context.Label,
            heuristicScore,
            context.Threats.Count,
            "Heuristic counter-coverage score from concrete per-opponent answers, broad removal, and reachable fusion outcomes. Exact opening fusion data and answer availability are combined conservatively; this is not a win probability.",
            safeOpponents,
            context.OpponentIds.Count,
            worstOpponentScore,
            openingCoverage);
    }

    private static double OpeningHandCoverage(int answerCopies, int deckSize)
    {
        if (answerCopies <= 0 || deckSize < 5)
        {
            return 0;
        }

        if (deckSize - answerCopies < 5)
        {
            return 1;
        }

        return 1 - ((double)DeckAnalyzer.Choose(deckSize - answerCopies, 5) / DeckAnalyzer.Choose(deckSize, 5));
    }

    private static bool IsBetterExactCandidate(
        ExactCandidate candidate,
        ExactCandidate incumbent,
        DeckOptimizationOptions options)
    {
        var comparer = new DeckObjectiveComparer(options.SafetyContext is not null, options.SecondarySafetyContext is not null);
        return comparer.Compare(new(candidate.Report, candidate.Safety, candidate.SecondarySafety, candidate.Deck),
            new(incumbent.Report, incumbent.Safety, incumbent.SecondarySafety, incumbent.Deck)) > 0;
    }

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

    private sealed record PreparedAssessment(CardStrategyAssessment Strategy, double Safety, double Secondary);
    private sealed record PreparedPair(double Synergy, double Fixed, int ResultId);

    private sealed record CardHeuristic(
        double Individual,
        int BaseStrength,
        int Flexibility,
        IReadOnlyDictionary<int, int> ResultCounts);

    private sealed record PreparedHeuristics(
        Dictionary<int, CardHeuristic> All,
        Dictionary<int, CardHeuristic> Intended);

    private sealed record SampledCandidate(int[] Deck, DeckObjective Objective);

    private sealed record ExactCandidate(
        int[] Deck,
        DeckAnalysisReport Report,
        DeckSafetyAssessment? Safety,
        DeckSafetyAssessment? SecondarySafety);
}
