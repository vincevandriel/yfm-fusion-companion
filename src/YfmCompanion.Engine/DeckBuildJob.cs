using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public sealed record DeckBuildCandidate(DeckOptimizationReport Report, long RequiredStarChips);
public sealed record DeckBuildProgress(DeckBuildState State, string Stage, TimeSpan Elapsed,
    TimeSpan SearchTimeConsumed, TimeSpan? SearchBudget, long CandidatesExamined,
    DeckBuildCandidate? Best, DeckBuildCandidate? EstimatedChallenger,
    long? CompletedHands = null, long? TotalHands = null, TimeSpan? EstimatedRemaining = null)
{
    // Measured extrapolation range, not a confidence interval or completion guarantee.
    public TimeSpan? EstimatedRemainingLow => EstimatedRemaining * .5;
    public TimeSpan? EstimatedRemainingHigh => EstimatedRemaining * 2;
    public BigInteger? ProofResolvedSpace { get; init; }
    public BigInteger? ProofTotalSpace { get; init; }
    public BigInteger? ProofLegalDecksEvaluated { get; init; }
}
public sealed record DeckBuildResult(DeckBuildState State, DeckBuildCandidate? Best, bool ProvenOptimal,
    long CandidatesExamined, string Methodology, string InputIdentity)
{
    public AnalysisCacheDiagnostics? SearchCache { get; init; }
    public BigInteger? ProofLegalDecksEvaluated { get; init; }
}

/// <summary>Single-worker frozen job; Pause/Resume retains timed-search state in this instance.</summary>
public sealed class DeckBuildJob
{
    private static readonly ConditionalWeakTable<FusionCatalog, CatalogIdentityBox> CatalogIdentities = [];
    private static readonly Lock CatalogIdentityLock = new();
    private readonly FusionCatalog _catalog;
    private readonly DeckBuildRequest _request;
    private readonly DeckObjectiveComparer _comparer;
    private readonly List<DeckBuildCandidate> _shortlist = [];
    private readonly Lock _control = new();
    private readonly string? _proofCheckpoint;
    private string _identity;
    private bool _catalogIdentityKnown;
    private OwnedDeckOptimizer? _optimizer;
    private DeckQuantitySpace? _space;
    private OwnedCardQuantity[] _available = [];
    private DeckBuildCandidate? _verified;
    private DeckBuildCandidate? _estimated;
    private CancellationTokenSource? _cancellation;
    private bool _pauseRequested, _stopRequested, _running, _searchDone, _verifyRequested;
    private TimeSpan _elapsed, _searchTime;
    private long _candidates;
    private uint _random;
    private volatile DeckBuildState _state = DeckBuildState.Preparing;

    public DeckBuildJob(FusionCatalog catalog, DeckBuildRequest request, string? proofCheckpointPath = null)
    {
        _catalog = catalog;
        _request = DeckProofSearch.Freeze(request);
        _identity = $"{DeckProofSearch.InputIdentity(_request)}:{DeckProofSearch.RulesVersion}:{DeckObjectiveComparer.Version}";
        _comparer = new(_request.Options.SafetyContext is not null, _request.Options.SecondarySafetyContext is not null);
        _random = unchecked((uint)_request.Options.RandomSeed) | 1U;
        _proofCheckpoint = proofCheckpointPath;
        if (_request.Mode == DeckSearchMode.ProveOptimal && string.IsNullOrWhiteSpace(proofCheckpointPath))
            throw new ArgumentException("Proof mode requires a durable checkpoint path.", nameof(proofCheckpointPath));
    }

    public DeckBuildState State => _state;
    public bool HasDurableCheckpoint => _proofCheckpoint is not null;
    /// <summary>The last completed operation, including a failed operation's retained best deck.</summary>
    public DeckBuildResult? LastResult { get; private set; }

    /// <summary>Legal unscored preview while campaign contexts are prepared.</summary>
    public static DeckBuildCandidate CreateLegalPreview(FusionCatalog catalog, DeckBuildRequest request,
        CancellationToken cancellationToken = default)
    {
        var previewJob = new DeckBuildJob(catalog, request with { Mode = DeckSearchMode.Quick });
        previewJob._space = new(catalog, previewJob._request.OwnedCards, previewJob._request.Options,
            request.UseStarChips, request.StarChips, cancellationToken);
        return previewJob.Preview(previewJob.FeasibleSeed(cancellationToken, _ => { }));
    }

    public void AdoptVerifiedIncumbent(DeckBuildResult previous)
    {
        lock (_control)
        {
            if (_running) throw new InvalidOperationException("Cannot change an active job's incumbent.");
            EnsureCatalogIdentity(default);
            if (previous.InputIdentity != _identity || previous.Best is not { Report.ExactAnalysis.IsExact: true } incumbent ||
                incumbent.Report.ExactAnalysis.TotalHands != 658008)
                throw new ArgumentException("Only an exactly verified result from identical frozen inputs and scoring options can be retained.", nameof(previous));
            var space = new DeckQuantitySpace(_catalog, _request.OwnedCards, _request.Options, _request.UseStarChips, _request.StarChips);
            if (!space.IsLegal(Expand(incumbent), out var spent) || spent != incumbent.RequiredStarChips)
                throw new ArgumentException("The incumbent is not legal for this request.", nameof(previous));
            if (_verified is null || Compare(incumbent, _verified) > 0)
                _verified = incumbent with { Report = OptimizationReportSnapshot.Freeze(incumbent.Report) };
        }
    }

    public Task<DeckBuildResult> VerifyBestAsync(IProgress<DeckBuildProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        lock (_control)
        {
            if (_running || _state is not (DeckBuildState.Completed or DeckBuildState.Cancelled) || _estimated is null)
                throw new InvalidOperationException("Finish or stop a search with a candidate before verifying it.");
            _verifyRequested = _searchDone = true;
            _state = DeckBuildState.Paused;
        }
        return RunAsync(progress, cancellationToken);
    }
    public static int MaximumWorkerCount => Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));
    public static TimeSpan? BudgetFor(DeckSearchMode mode) => mode switch
    {
        DeckSearchMode.Quick => TimeSpan.FromSeconds(5),
        DeckSearchMode.Balanced => TimeSpan.FromMinutes(1),
        DeckSearchMode.Thorough => TimeSpan.FromMinutes(15),
        DeckSearchMode.ProveOptimal => null,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public void Pause()
    {
        lock (_control)
        {
            if (!_running || _state is not (DeckBuildState.Preparing or DeckBuildState.Searching or DeckBuildState.Verifying)) return;
            _pauseRequested = true;
            _state = DeckBuildState.Pausing;
            _cancellation?.Cancel();
        }
    }

    public void StopAndKeepBest()
    {
        lock (_control)
        {
            if (!_running && _state == DeckBuildState.Paused)
            {
                _state = DeckBuildState.Cancelled;
                return;
            }
            if (!_running || _state is not (DeckBuildState.Preparing or DeckBuildState.Searching or DeckBuildState.Verifying or DeckBuildState.Pausing)) return;
            _stopRequested = true;
            _cancellation?.Cancel();
        }
    }

    public Task<DeckBuildResult> RunAsync(IProgress<DeckBuildProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        lock (_control)
        {
            if (_running || _state is not (DeckBuildState.Preparing or DeckBuildState.Paused))
                throw new InvalidOperationException("Only a new or paused job can be started.");
            _running = true;
            _pauseRequested = _stopRequested = false;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _state = DeckBuildState.Preparing;
        }
        return Task.Run(() => Run(progress, _cancellation.Token), CancellationToken.None);
    }

    private DeckBuildResult Run(IProgress<DeckBuildProgress>? progress, CancellationToken token)
    {
        var elapsed = Stopwatch.StartNew();
        var stageClock = Stopwatch.StartNew();
        var lastReport = Stopwatch.StartNew();
        var budget = BudgetFor(_request.Mode);
        DeckProofProgress? proofProgress = null;
        DeckBuildCandidate? Best() => _verified ?? _estimated;
        void Report(string stage, long? hands = null, long? total = null, bool force = false)
        {
            if (!force && lastReport.ElapsedMilliseconds < 200) return;
            TimeSpan? remaining = _request.Mode != DeckSearchMode.ProveOptimal && _state == DeckBuildState.Verifying && hands is > 0 && total > hands
                ? TimeSpan.FromMilliseconds(stageClock.Elapsed.TotalMilliseconds * (total.Value - hands.Value) / hands.Value) : null;
            progress?.Report(new(_state, stage, _elapsed + elapsed.Elapsed, _searchTime, budget, _candidates,
                Best(), _verified is null ? null : _estimated, hands, total, remaining)
            {
                ProofResolvedSpace = proofProgress?.ResolvedSpace,
                ProofTotalSpace = proofProgress?.TotalSpace,
                ProofLegalDecksEvaluated = proofProgress?.LegalDecksEvaluated
            });
            lastReport.Restart();
        }
        DeckBuildResult Finish(bool proven = false) => LastResult = new(_state, Best(), proven, _candidates,
            proven ? "Proven optimal for the frozen model and inputs; not a duel-win guarantee."
            : "Best found. Sampled statistics are estimates; exact hand verification does not prove deck optimality.", _identity)
        { SearchCache = _optimizer?.CacheDiagnostics, ProofLegalDecksEvaluated = proofProgress?.LegalDecksEvaluated };
        try
        {
            Report("Preparing", force: true);
            if (_request.Mode == DeckSearchMode.ProveOptimal)
            {
                var proof = new DeckProofSearch(_catalog).RunWithIncumbentAsync(_request, _proofCheckpoint!, _verified,
                    new InlineProgress<DeckProofProgress>(p =>
                    {
                        proofProgress = p;
                        _state = p.State;
                        if (p.Best is not null) _verified = new(p.Best, p.RequiredStarChips);
                        // Proof progress has BigInteger space counts; no fabricated overall percentage.
                        Report($"Proof: {p.ResolvedSpace} / {p.TotalSpace} space resolved; {p.LegalDecksEvaluated} legal decks", p.CompletedHands, p.TotalHands);
                    }), token).GetAwaiter().GetResult();
                _state = _stopRequested ? DeckBuildState.Cancelled : proof.State;
                _verified = proof.Best is null ? null : new(proof.Best, proof.RequiredStarChips);
                EnsureCatalogIdentity(default);
                Report("Proof result ready for installation", force: true);
                return Finish(proof.ProvenOptimal);
            }

            EnsureCatalogIdentity(token);
            Report("Catalog identity validated", force: true);
            token.ThrowIfCancellationRequested();
            _space ??= new(_catalog, _request.OwnedCards, _request.Options, _request.UseStarChips, _request.StarChips, token);
            _available = [.. _space.Capacities.Select(c => new OwnedCardQuantity(c.CardId, c.Capacity))];
            Report("Inventory and scoring prepared", force: true);
            if (_estimated is null)
            {
                var seedClock = Stopwatch.StartNew();
                var seed = _verified is null ? FeasibleSeed(token, p => Report(p.Stage, force: false)) : Expand(_verified);
                _state = DeckBuildState.Searching;
                // Make the legal 40-card result visible immediately. Its zero-sample
                // report is explicitly an unevaluated preview and is replaced before
                // it can participate in comparisons or finalist selection.
                _estimated = Preview(seed);
                _searchTime += seedClock.Elapsed;
                Report("First legal deck found; evaluation pending", force: true);
            }
            token.ThrowIfCancellationRequested();
            _optimizer ??= new(_catalog);

            _state = DeckBuildState.Searching;
            var search = Stopwatch.StartNew();
            var priorSearch = _searchTime;
            using (var searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var remaining = budget!.Value - _searchTime;
                if (remaining > TimeSpan.Zero && !_searchDone) searchCancellation.CancelAfter(remaining);
                else _searchDone = true;
                try
                {
                    while (!_searchDone)
                    {
                        searchCancellation.Token.ThrowIfCancellationRequested();
                        // A pause can retain the legal preview before any scoring.
                        // Score it under the same budget before comparing challengers.
                        if (_estimated!.Report.ExactAnalysis.TotalHands == 0)
                        {
                            _estimated = Evaluate(Expand(_estimated), exact: false, searchCancellation.Token,
                                p => Report(p.Stage, p.CompletedHands, p.TotalHands));
                            _candidates++;
                            Retain(_estimated);
                            _estimated = Describe(_estimated);
                            _searchTime = priorSearch + search.Elapsed;
                            Report("Initial deck evaluated", force: true);
                            continue;
                        }
                        var deck = Expand(_estimated!);
                        // Deterministic multi-swap restarts supplement single-swap local improvements.
                        var swaps = _candidates % 16 == 0 ? 12 : _candidates % 5 == 0 ? 3 : 1;
                        for (var i = 0; i < swaps; i++) deck[Next(deck.Length)] = _available[Next(_available.Length)].CardId;
                        if (_space.IsLegal(deck, out _))
                        {
                            var candidate = Evaluate(deck, exact: false, searchCancellation.Token, p => Report(p.Stage, p.CompletedHands, p.TotalHands));
                            _candidates++;
                            if (Compare(candidate, _estimated) > 0) _estimated = Describe(candidate);
                            Retain(candidate);
                        }
                        _searchTime = priorSearch + search.Elapsed;
                        Report("Search budget consumed");
                        if (_searchTime >= budget) _searchDone = true;
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && searchCancellation.IsCancellationRequested)
                { _searchDone = true; }
                finally { _searchTime = priorSearch + search.Elapsed; }
            }
            token.ThrowIfCancellationRequested();
            if (_request.Mode != DeckSearchMode.Quick || _verifyRequested)
            {
                _state = DeckBuildState.Verifying;
                var finalists = _request.Mode == DeckSearchMode.Quick || _shortlist.Count == 0
                    ? new[] { _estimated! } : [.. _shortlist];
                foreach (var finalist in finalists)
                {
                    stageClock.Restart();
                    Report("Verifying shortlisted deck", 0, 658008, force: true);
                    var candidate = Evaluate(Expand(finalist), exact: true, token, p => Report(p.Stage, p.CompletedHands, p.TotalHands));
                    if (_verified is null || Compare(candidate, _verified) > 0) _verified = candidate;
                }
            }
            _state = DeckBuildState.Completed;
            Report("Result ready for installation", force: true);
            return Finish();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _state = _pauseRequested && !_stopRequested ? DeckBuildState.Paused : DeckBuildState.Cancelled;
            Report(_state == DeckBuildState.Paused ? "Paused; completed work retained" : "Stopped; best completed deck retained", force: true);
            return Finish();
        }
        catch
        {
            _state = DeckBuildState.Failed;
            Finish();
            Report("Failed; completed best deck retained", force: true);
            throw;
        }
        finally
        {
            _elapsed += elapsed.Elapsed;
            lock (_control) { _running = false; _cancellation?.Dispose(); _cancellation = null; }
        }
    }

    private int[] FeasibleSeed(CancellationToken token, Action<DeckOptimizationProgress> progress)
    {
        var allowed = _space!.Capacities.ToDictionary(c => c.CardId, c => Math.Min(c.Owned, c.Capacity));
        var capacity = allowed.Values.Sum();
        long spent = 0;
        foreach (var card in _space.Capacities.Where(c => c.Capacity > c.Owned && c.PurchaseCost is not null)
            .OrderBy(c => c.PurchaseCost).ThenBy(c => c.CardId))
        {
            token.ThrowIfCancellationRequested();
            if (capacity >= 40) break;
            if (spent + card.PurchaseCost!.Value > _request.StarChips) break;
            spent += card.PurchaseCost.Value;
            allowed[card.CardId]++;
            capacity++;
        }
        if (capacity < 40) throw new InvalidOperationException("This collection and eligible affordable purchases cannot supply 40 legal copies.");
        progress(new("Preparing first legal deck", 0, 1));
        // Publish a usable incumbent before the more expensive strategy and
        // opponent assessments. Search immediately improves this deterministic
        // connectivity/strength seed under the full shared objective.
        var fusionDegree = new Dictionary<int, int>();
        foreach (var pair in _catalog.FusionPairs)
        {
            fusionDegree[pair.MaterialLowId] = fusionDegree.GetValueOrDefault(pair.MaterialLowId) + 1;
            fusionDegree[pair.MaterialHighId] = fusionDegree.GetValueOrDefault(pair.MaterialHighId) + 1;
        }
        var seed = allowed
            .Where(item => item.Value > 0)
            .OrderByDescending(item => fusionDegree.GetValueOrDefault(item.Key))
            .ThenByDescending(item => Math.Max(_catalog.GetCard(item.Key).Attack, _catalog.GetCard(item.Key).Defense))
            .ThenBy(item => item.Key)
            .SelectMany(item => Enumerable.Repeat(item.Key, item.Value))
            .Take(40)
            .Order()
            .ToArray();
        if (seed.Length != 40 || !_space.IsLegal(seed, out _))
            throw new InvalidDataException("The deterministic early deck did not satisfy the frozen legal deck space.");
        progress(new("Preparing first legal deck", 1, 1));
        return seed;
    }

    private void EnsureCatalogIdentity(CancellationToken token)
    {
        if (_catalogIdentityKnown) return;
        if (!CatalogIdentities.TryGetValue(_catalog, out var box))
        {
            var computed = DeckProofSearch.CatalogIdentity(_catalog, token);
            lock (CatalogIdentityLock)
            {
                if (!CatalogIdentities.TryGetValue(_catalog, out box))
                {
                    box = new CatalogIdentityBox(computed);
                    CatalogIdentities.Add(_catalog, box);
                }
            }
        }
        _identity += $":{box.Value}";
        _catalogIdentityKnown = true;
    }

    private sealed record CatalogIdentityBox(string Value);

    private DeckBuildCandidate Preview(int[] deck)
    {
        if (!_space!.IsLegal(deck, out var spent)) throw new InvalidDataException("Search produced an illegal deck.");
        var entries = deck.GroupBy(id => id)
            .Select(group => new OptimizedDeckEntry(_catalog.GetCard(group.Key), group.Count(), "Evaluation pending"))
            .ToArray();
        var analysis = new DeckAnalysisReport(deck.Length, Math.Min(5, deck.Length), 0, 0, 0, 0, 0, 0, 0, [])
        {
            IsExact = false,
            SampleCount = 0
        };
        return new(new(entries, analysis, [], [], [], null, _request.Options.Profile,
            _request.Options.RandomSeed, null, null), spent);
    }

    private DeckBuildCandidate Evaluate(int[] deck, bool exact, CancellationToken token, Action<DeckOptimizationProgress> progress)
    {
        if (!_space!.IsLegal(deck, out var spent)) throw new InvalidDataException("Search produced an illegal deck.");
        var candidate = new DeckBuildCandidate(_optimizer!.EvaluateCandidate(deck, _request.Options, exact,
            new InlineProgress<DeckOptimizationProgress>(progress), token), spent);
        return exact ? Describe(candidate) : candidate;
    }
    private DeckBuildCandidate Describe(DeckBuildCandidate candidate) => candidate with
    { Report = OptimizationReportSnapshot.Freeze(_optimizer!.DescribeCandidate(candidate.Report, _available, _request.Options)) };
    private int Compare(DeckBuildCandidate first, DeckBuildCandidate second) => _comparer.Compare(
        DeckObjectiveComparer.FromReport(first.Report, first.RequiredStarChips), DeckObjectiveComparer.FromReport(second.Report, second.RequiredStarChips));
    private void Retain(DeckBuildCandidate candidate)
    {
        var limit = _request.Mode == DeckSearchMode.Thorough ? 8 : 2;
        if (_shortlist.Count >= limit && Compare(candidate, _shortlist[^1]) <= 0) return;
        var key = string.Join(',', Expand(candidate));
        if (_shortlist.Any(c => string.Join(',', Expand(c)) == key)) return;
        _shortlist.Add(candidate);
        _shortlist.Sort((a, b) => -Compare(a, b));
        if (_shortlist.Count > limit) _shortlist.RemoveRange(limit, _shortlist.Count - limit);
    }
    private static int[] Expand(DeckBuildCandidate candidate) => [.. candidate.Report.Deck.SelectMany(e => Enumerable.Repeat(e.Card.Id, e.Copies)).Order()];
    private int Next(int bound)
    {
        _random ^= _random << 13; _random ^= _random >> 17; _random ^= _random << 5;
        return (int)(_random % (uint)bound);
    }
}
