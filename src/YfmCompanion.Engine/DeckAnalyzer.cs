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
    private readonly int _workerCount;
    private readonly long _workerCacheBudget;
    private DeckAnalyzer[]? _workers;
    private int _stamp;
    private int[] _hand = [];
    private bool _includeGlitches;
    private CancellationToken _token;
    private int _bestSetupAttack;
    private int _bestBodyAttack;

    public DeckAnalyzer(FusionCatalog catalog, long cacheByteLimit = 256L * 1024 * 1024, int workerCount = 1)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentOutOfRangeException.ThrowIfNegative(cacheByteLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workerCount);
        _catalog = catalog;
        _workerCount = workerCount;
        _workerCacheBudget = workerCount == 1 ? 0 : cacheByteLimit / 2;
        _cache = new(cacheByteLimit - _workerCacheBudget);
    }

    public AnalysisCacheDiagnostics CacheDiagnostics
    {
        get
        {
            lock (_gate)
            {
                var diagnostics = new[] { _cache.Diagnostics }.Concat(_workers?.Select(w => w.CacheDiagnostics) ?? []);
                return new(diagnostics.Sum(d => d.AccountedBytes), _cache.Diagnostics.LimitBytes + _workerCacheBudget,
                    diagnostics.Sum(d => d.Hits), diagnostics.Sum(d => d.Misses), diagnostics.Sum(d => d.Entries));
            }
        }
    }

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
            if (samples is null && total >= 4096 && _workerCount > 1)
            {
                AnalyzeParallel(deck, handSize, total, glitches, progress, token);
            }
            else if (total > 0 && samples is { } count)
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

    // Enumerate in the serial order and merge contiguous partitions in that same order.
    // This preserves the first representative when equally good routes tie.
    private void AnalyzeParallel(int[] deck, int handSize, long total, bool glitches,
        IProgress<DeckAnalysisProgress>? progress, CancellationToken token)
    {
        var work = new List<(ulong Hand, long Weight)>();
        var groups = deck.GroupBy(id => id).Select(g => (Id: g.Key, Count: g.Count())).ToArray();
        var suffix = new int[groups.Length + 1];
        for (var i = groups.Length - 1; i >= 0; i--) suffix[i] = suffix[i + 1] + groups[i].Count;
        void Enumerate(int group, int used, long weight, ulong hand)
        {
            token.ThrowIfCancellationRequested();
            if (used == handSize) { work.Add((hand, weight)); return; }
            if (group == groups.Length || suffix[group] < handSize - used) return;
            var minimum = Math.Max(0, handSize - used - suffix[group + 1]);
            var maximum = Math.Min(groups[group].Count, handSize - used);
            for (var take = 0; take <= maximum; take++)
            {
                if (take >= minimum) Enumerate(group + 1, used + take,
                    checked(weight * Choose(groups[group].Count, take)), hand);
                hand |= (ulong)groups[group].Id << ((used + take) * 10);
            }
        }
        Enumerate(0, 0, 1, 0);
        // Very duplicate-heavy decks have few distinct hands and do not justify scheduling.
        var count = Math.Min(_workerCount, Math.Max(1, work.Count / 128));
        _workers ??= Enumerable.Range(0, _workerCount)
            .Select(_ => new DeckAnalyzer(_catalog, _workerCacheBudget / _workerCount)).ToArray();
        long completed = 0;
        var clock = Stopwatch.StartNew();
        var midpointReported = false;
        using var workCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var workToken = workCancellation.Token;
        var task = Task.Run(() => Parallel.For(0, count,
            new ParallelOptions { MaxDegreeOfParallelism = count, CancellationToken = workToken }, index =>
        {
            var worker = _workers[index];
            worker._accumulator.Reset(total);
            var hand = new int[handSize];
            var start = (int)((long)work.Count * index / count);
            var end = (int)((long)work.Count * (index + 1) / count);
            long pending = 0;
            for (var i = start; i < end; i++)
            {
                workToken.ThrowIfCancellationRequested();
                var item = work[i];
                for (var h = 0; h < handSize; h++) hand[h] = (int)(item.Hand >> (h * 10) & 1023);
                worker._accumulator.Add(worker.EvaluateHand(hand, glitches, workToken), item.Weight, _catalog);
                pending += item.Weight;
                if ((i - start) % 128 != 127 && i != end - 1) continue;
                Interlocked.Add(ref completed, pending);
                pending = 0;
            }
        }));
        try
        {
            // Preserve the serial callback context. A callback can inspect diagnostics
            // without blocking a worker on the coordinator's analyzer lock.
            while (!task.IsCompleted)
            {
                var done = Interlocked.Read(ref completed);
                var midpoint = !midpointReported && done >= total / 2 && done < total;
                if (midpoint || clock.ElapsedMilliseconds >= 200)
                {
                    midpointReported |= midpoint;
                    progress?.Report(new(done, total));
                    clock.Restart();
                }
                try { task.Wait(10); }
                catch (AggregateException) { break; }
            }
            task.GetAwaiter().GetResult();
        }
        catch
        {
            // A throwing callback must not leave workers modifying reusable scratch.
            workCancellation.Cancel();
            try { task.GetAwaiter().GetResult(); }
            catch (Exception) { /* Preserve the coordinator's original failure. */ }
            throw;
        }
        token.ThrowIfCancellationRequested();
        for (var i = 0; i < count; i++) _accumulator.Merge(_workers[i]._accumulator);
    }

    private HandEvaluation EvaluateHand(int[] hand, bool glitches, CancellationToken token)
    {
        ulong packed = (ulong)hand.Length << 50;
        for (var i = 0; i < hand.Length; i++) packed |= (ulong)hand[i] << (i * 10);
        var key = new AnalysisCacheKey(packed | (glitches ? 1UL << 54 : 0));
        if (_cache.TryGet<HandEvaluation>(key, out var cached)) return cached!;
        if (_stamp == int.MaxValue) { Array.Clear(_stamps); _stamp = 0; }
        _stamp++;
        _outcomes.Clear();
        _bestSetupAttack = 0;
        _bestBodyAttack = 0;
        _hand = hand;
        _includeGlitches = glitches;
        _token = token;
        for (var start = 0; start < hand.Length; start++) Explore(hand[start], 1 << start, 1, (ulong)hand[start], 0, false);
        // Empty hands are cheap to recompute and common in sparse starter inventories.
        // Do not spend an LRU node/key on each of hundreds of thousands of empty outcomes.
        var routes = _outcomes.Count == 0 ? [] : new HandRoute[_outcomes.Count];
        for (var i = 0; i < routes.Length; i++) routes[i] = _routes[_outcomes[i]];
        var clear = hand.Any(id => id is 336 or 337);
        var broadRemoval = hand.Any(id => id is 336 or 337 or 686);
        var hasMonster = _bestBodyAttack > 0 || hand.Any(id => _catalog.GetCard(id).PrimaryType is not ("Equip" or "Magic" or "Trap" or "Ritual"));
        var value = new HandEvaluation(routes, _bestSetupAttack, clear, broadRemoval, _bestBodyAttack, hasMonster);
        // Setup/removal-only hands also carry meaningful work and must be cached.
        if (routes.Length > 0 || _bestSetupAttack >= 3500 || broadRemoval)
            _cache.Add(key, value, 80L + routes.Length * 40L);
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
        ConsiderSetup(current, mask);
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

    private void ConsiderSetup(int current, int consumedMask)
    {
        var monster = _catalog.GetCard(current);
        if (monster.Attack <= 0) return;
        _bestBodyAttack = Math.Max(_bestBodyAttack, monster.Attack);
        var attack = monster.Attack;
        var bestTerrain = 0;
        for (var i = 0; i < _hand.Length; i++)
        {
            if ((consumedMask & (1 << i)) != 0) continue;
            var support = _hand[i];
            if (_catalog.CanEquip(support, current)) attack += support == 657 ? 1000 : 500;
            if (ForbiddenMemoriesStrategyEvaluator.IsFieldCard(support))
                bestTerrain = Math.Max(bestTerrain, ForbiddenMemoriesStrategyEvaluator.GetFieldModifier(support, monster.PrimaryType));
        }
        // Each physical equip is consumed once, after the final monster is formed.
        // One terrain is chosen, never stacked. Nothing is assumed already active.
        _bestSetupAttack = Math.Max(_bestSetupAttack, attack + bestTerrain);
    }

    private sealed record HandEvaluation(HandRoute[] Routes, int SetupAttack, bool BoardClear, bool BroadRemoval, int BodyAttack, bool HasMonster);

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
        private long _setup3500, _setup4500, _clear, _removal, _setupOrClear, _setupSum;
        private int _maximumSetup;
        private long _endgame, _endgameOrClear, _body2800, _noMonster;
        public long Completed { get; private set; }

        public void Reset(long total)
        {
            _total = total;
            Array.Clear(_counts);
            _any = _at2000 = _at2500 = _at2800 = _at3000 = _sum = Completed = 0;
            _setup3500 = _setup4500 = _clear = _removal = _setupOrClear = _setupSum = 0;
            _maximumSetup = 0;
            _endgame = _endgameOrClear = _body2800 = _noMonster = 0;
            // Representatives are structs; entries are replaced on the first count.
            // Reuse these large scratch arrays under the analyzer lock. Returned
            // reports own their materialized arrays and never reference this scratch.
        }

        public void Add(HandEvaluation evaluation, long weight, FusionCatalog catalog)
        {
            var routes = evaluation.Routes;
            if (evaluation.SetupAttack >= 3500) _setup3500 += weight;
            if (evaluation.SetupAttack >= 4500) _setup4500 += weight;
            if (evaluation.SetupAttack > 4500) _endgame += weight;
            if (evaluation.SetupAttack > 4500 || evaluation.BoardClear) _endgameOrClear += weight;
            if (evaluation.BodyAttack >= 2800) _body2800 += weight;
            if (!evaluation.HasMonster) _noMonster += weight;
            if (evaluation.BoardClear) _clear += weight;
            if (evaluation.BroadRemoval) _removal += weight;
            if (evaluation.SetupAttack >= 3500 || evaluation.BoardClear) _setupOrClear += weight;
            _setupSum += evaluation.SetupAttack * weight;
            _maximumSetup = Math.Max(_maximumSetup, evaluation.SetupAttack);
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

        public void Merge(Accumulator other)
        {
            for (var i = 0; i < _counts.Length; i++)
            {
                if (other._counts[i] == 0) continue;
                if (_counts[i] == 0 || other._representatives[i].BetterThan(_representatives[i]))
                    _representatives[i] = other._representatives[i];
                _counts[i] += other._counts[i];
            }
            _any += other._any; _at2000 += other._at2000; _at2500 += other._at2500;
            _at2800 += other._at2800; _at3000 += other._at3000; _sum += other._sum;
            _setup3500 += other._setup3500; _setup4500 += other._setup4500;
            _clear += other._clear; _removal += other._removal;
            _setupOrClear += other._setupOrClear; _setupSum += other._setupSum;
            _maximumSetup = Math.Max(_maximumSetup, other._maximumSetup);
            _endgame += other._endgame; _endgameOrClear += other._endgameOrClear;
            _body2800 += other._body2800; _noMonster += other._noMonster;
            Completed += other.Completed;
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
            { TotalBestFusionAttack = _sum, IsExact = samples is null, SampleCount = samples ?? 0,
                HandsWith3500Setup = _setup3500, HandsWith4500Setup = _setup4500,
                HandsWithBoardClear = _clear, HandsWithBroadRemoval = _removal,
                HandsWith3500SetupOrBoardClear = _setupOrClear, TotalBestSetupAttack = _setupSum, MaximumSetupAttack = _maximumSetup,
                HandsWithEndgamePower = _endgame, HandsWithEndgamePowerOrBoardClear = _endgameOrClear,
                HandsWith2800Body = _body2800, HandsWithNoMonster = _noMonster };
        }
    }
}
