using System.Diagnostics;
using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public sealed class DeckAnalyzer
{
    private const int Offset = 723;
    private readonly FusionCatalog _catalog;
    private readonly BoundedAnalysisCache _cache;
    private readonly object _gate = new();
    private readonly HandRoute[] _routes = new HandRoute[2169];
    private readonly int[] _stamps = new int[2169];
    private readonly List<int> _outcomes = new(64);
    private readonly Accumulator _accumulator = new();
    private int _stamp;
    private int[] _hand = [];
    private bool _includeGlitches;
    private CancellationToken _token;

    public DeckAnalyzer(FusionCatalog catalog, long cacheByteLimit = 256L * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentOutOfRangeException.ThrowIfNegative(cacheByteLimit);
        _catalog = catalog;
        _cache = new(cacheByteLimit);
    }

    public AnalysisCacheDiagnostics CacheDiagnostics { get { lock (_gate) return _cache.Diagnostics; } }

    public DeckAnalysisReport Analyze(IEnumerable<int> deckCardIds, bool includeGlitches = true,
        IProgress<DeckAnalysisProgress>? progress = null, CancellationToken cancellationToken = default) =>
        AnalyzeCore(deckCardIds, includeGlitches, null, 0, progress, cancellationToken);

    public DeckAnalysisReport AnalyzeSampled(IEnumerable<int> deckCardIds, int sampleCount, int seed,
        bool includeGlitches = true, IProgress<DeckAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleCount);
        return AnalyzeCore(deckCardIds, includeGlitches, sampleCount, seed, progress, cancellationToken);
    }

    private DeckAnalysisReport AnalyzeCore(IEnumerable<int> deckCardIds, bool glitches, int? samples, int seed,
        IProgress<DeckAnalysisProgress>? progress, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(deckCardIds);
        token.ThrowIfCancellationRequested();
        var deck = deckCardIds.Order().ToArray();
        if (deck.Length > 40) throw new ArgumentException("A deck may contain at most 40 cards.", nameof(deckCardIds));
        foreach (var id in deck) _ = _catalog.GetCard(id);
        while (!Monitor.TryEnter(_gate, 50)) token.ThrowIfCancellationRequested();
        try
        {
            token.ThrowIfCancellationRequested();
            var handSize = Math.Min(5, deck.Length);
            var total = deck.Length < 2 ? 0 : samples ?? Choose(deck.Length, handSize);
            progress?.Report(new(0, total));
            token.ThrowIfCancellationRequested();
            var key = new AnalysisCacheKey(0, $"D:{glitches}:{samples}:{seed}:{string.Join(',', deck)}");
            if (_cache.TryGet<DeckAnalysisReport>(key, out var cached))
            {
                progress?.Report(new(total, total));
                return cached!;
            }
            var accumulator = _accumulator;
            accumulator.Reset(total);
            var watch = Stopwatch.StartNew();
            var hand = new int[handSize];
            void Evaluate(long weight)
            {
                token.ThrowIfCancellationRequested();
                accumulator.Add(EvaluateHand(hand, glitches, token), weight, _catalog);
                // Copy multiplicities can jump over count thresholds; elapsed time cannot miss them.
                if (watch.ElapsedMilliseconds >= 200)
                {
                    progress?.Report(new(accumulator.Completed, total));
                    watch.Restart();
                }
            }
            if (total > 0 && samples is { } count)
            {
                var random = new Random(seed);
                var positions = new int[deck.Length];
                for (var sample = 0; sample < count; sample++)
                {
                    for (var p = 0; p < positions.Length; p++) positions[p] = p;
                    for (var p = 0; p < handSize; p++)
                    {
                        var next = random.Next(p, positions.Length);
                        (positions[p], positions[next]) = (positions[next], positions[p]);
                        hand[p] = deck[positions[p]];
                    }
                    Array.Sort(hand);
                    Evaluate(1);
                }
            }
            else if (total > 0)
            {
                var groups = deck.GroupBy(id => id).Select(g => (Id: g.Key, Count: g.Count())).ToArray();
                var suffix = new int[groups.Length + 1];
                for (var i = groups.Length - 1; i >= 0; i--) suffix[i] = suffix[i + 1] + groups[i].Count;
                void Enumerate(int group, int used, long weight)
                {
                    token.ThrowIfCancellationRequested();
                    if (used == handSize) { Evaluate(weight); return; }
                    if (group == groups.Length || suffix[group] < handSize - used) return;
                    var minimum = Math.Max(0, handSize - used - suffix[group + 1]);
                    var maximum = Math.Min(groups[group].Count, handSize - used);
                    for (var take = minimum; take <= maximum; take++)
                    {
                        Array.Fill(hand, groups[group].Id, used, take);
                        Enumerate(group + 1, used + take, checked(weight * Choose(groups[group].Count, take)));
                    }
                }
                Enumerate(0, 0, 1);
            }
            if (accumulator.Completed != total) throw new InvalidDataException("Hand accounting did not match its expected total.");
            var report = accumulator.Report(deck.Length, handSize, samples, _catalog);
            _cache.Add(key, report, 512 + report.FusionResults.Count * 1536L);
            progress?.Report(new(total, total));
            return report;
        }
        finally { Monitor.Exit(_gate); }
    }

    private HandRoute[] EvaluateHand(int[] hand, bool glitches, CancellationToken token)
    {
        ulong packed = (ulong)hand.Length << 50;
        for (var i = 0; i < hand.Length; i++) packed |= (ulong)hand[i] << (i * 10);
        var key = new AnalysisCacheKey(packed | (glitches ? 1UL << 54 : 0));
        if (_cache.TryGet<HandRoute[]>(key, out var cached)) return cached!;
        if (_stamp == int.MaxValue) { Array.Clear(_stamps); _stamp = 0; }
        _stamp++;
        _outcomes.Clear();
        _hand = hand;
        _includeGlitches = glitches;
        _token = token;
        for (var start = 0; start < hand.Length; start++) Explore(hand[start], 1 << start, 1, (ulong)hand[start], 0, false);
        // Empty hands are cheap to recompute and common in sparse starter inventories.
        // Do not spend an LRU node/key on each of hundreds of thousands of empty outcomes.
        var value = _outcomes.Count == 0 ? [] : new HandRoute[_outcomes.Count];
        for (var i = 0; i < value.Length; i++) value[i] = _routes[_outcomes[i]];
        if (value.Length > 0) _cache.Add(key, value, 32L + value.Length * 40L);
        return value;
    }

    private void Record(HandRoute route)
    {
        if (_stamps[route.Outcome] != _stamp)
        {
            _stamps[route.Outcome] = _stamp;
            _outcomes.Add(route.Outcome);
            _routes[route.Outcome] = route;
        }
        else if (route.BetterThan(_routes[route.Outcome])) _routes[route.Outcome] = route;
    }

    private void Explore(int current, int mask, int depth, ulong materials, ulong results, bool hasGlitch)
    {
        _token.ThrowIfCancellationRequested();
        for (var next = 0; next < _hand.Length; next++)
        {
            if ((mask & (1 << next)) != 0) continue;
            if (_catalog.CanEquip(_hand[next], current))
            {
                var bonus = _hand[next] == 657 ? 1000 : 500;
                Record(new(current + (bonus == 1000 ? 2 : 1) * Offset,
                    materials | (ulong)_hand[next] << (depth * 10),
                    results | (ulong)current << ((depth - 1) * 10), (byte)(depth + 1), hasGlitch));
            }
            if (depth == 1 && (_hand[next] < current || (_hand[next] == current && (1 << next) <= mask))) continue;
            if (!_catalog.TryResolvePair(current, _hand[next], _includeGlitches, out var result, out var glitch)) continue;
            var m = materials | (ulong)_hand[next] << (depth * 10);
            var r = results | (ulong)result << ((depth - 1) * 10);
            Record(new(result, m, r, (byte)(depth + 1), hasGlitch || glitch));
            Explore(result, mask | (1 << next), depth + 1, m, r, hasGlitch || glitch);
        }
    }

    public static long Choose(int population, int selected)
    {
        if (selected < 0 || population < 0 || selected > population) return 0;
        selected = Math.Min(selected, population - selected);
        long result = 1;
        for (var i = 1; i <= selected; i++) result = checked(result * (population - selected + i) / i);
        return result;
    }

    private readonly record struct HandRoute(int Outcome, ulong Materials, ulong Results, byte Count, bool Glitch)
    {
        public int CardId => Outcome % Offset;
        public int Bonus => Outcome / Offset * 500;
        public bool BetterThan(HandRoute other) => Glitch.CompareTo(other.Glitch) < 0 || (Glitch == other.Glitch && Count < other.Count);
        public DeckFusionRoute Materialize(FusionCatalog catalog)
        {
            var materials = new Card[Count];
            var results = new Card[Count - 1];
            for (var i = 0; i < materials.Length; i++) materials[i] = catalog.GetCard((int)(Materials >> (i * 10) & 1023));
            for (var i = 0; i < results.Length; i++) results[i] = catalog.GetCard((int)(Results >> (i * 10) & 1023));
            return new(Array.AsReadOnly(materials), Array.AsReadOnly(results), Glitch, Bonus > 0, Bonus);
        }
    }

    private sealed class Accumulator
    {
        private long _total;
        private readonly long[] _counts = new long[2169];
        private readonly HandRoute[] _representatives = new HandRoute[2169];
        private long _any, _at2000, _at2500, _at2800, _at3000, _sum;
        public long Completed { get; private set; }

        public void Reset(long total)
        {
            _total = total;
            Array.Clear(_counts);
            _any = _at2000 = _at2500 = _at2800 = _at3000 = _sum = Completed = 0;
            // Representatives are structs; entries are replaced on the first count.
            // Reuse these large scratch arrays under the analyzer lock. Returned
            // reports own their materialized arrays and never reference this scratch.
        }

        public void Add(HandRoute[] routes, long weight, FusionCatalog catalog)
        {
            var best = 0;
            foreach (var route in routes)
            {
                if (_counts[route.Outcome] == 0 || route.BetterThan(_representatives[route.Outcome])) _representatives[route.Outcome] = route;
                _counts[route.Outcome] += weight;
                best = Math.Max(best, catalog.GetCard(route.CardId).Attack + route.Bonus);
            }
            if (routes.Length > 0) _any += weight;
            if (best >= 2000) _at2000 += weight;
            if (best >= 2500) _at2500 += weight;
            if (best >= 2800) _at2800 += weight;
            if (best >= 3000) _at3000 += weight;
            _sum += best * weight;
            Completed += weight;
        }

        public DeckAnalysisReport Report(int deckSize, int handSize, int? samples, FusionCatalog catalog)
        {
            var results = Enumerable.Range(1, 2168).Where(i => _counts[i] > 0).Select(i =>
            {
                var route = _representatives[i];
                return new DeckFusionResult(catalog.GetCard(route.CardId), _counts[i], _total,
                    route.Materialize(catalog), route.Bonus, route.Bonus);
            }).OrderByDescending(r => r.EffectiveAttack).ThenByDescending(r => r.EffectiveDefense)
                .ThenByDescending(r => r.Probability).ThenBy(r => r.Result.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            return new(deckSize, handSize, _total, _any, _at2000, _at2500, _at2800, _at3000,
                _total == 0 ? 0 : (double)_sum / _total, Array.AsReadOnly(results))
            { TotalBestFusionAttack = _sum, IsExact = samples is null, SampleCount = samples ?? 0 };
        }
    }
}
