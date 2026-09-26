using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public enum DeckSearchMode { Quick, Balanced, Thorough, ProveOptimal }
public enum DeckBuildState { Preparing, Searching, Verifying, Pausing, Paused, Completed, Cancelled, Failed }
public sealed record DeckBuildRequest(IReadOnlyList<OwnedCardQuantity> OwnedCards, DeckOptimizationOptions Options,
    DeckSearchMode Mode = DeckSearchMode.Balanced, bool UseStarChips = false, uint StarChips = 0, string SourceIdentity = "manual");
public sealed record DeckProofProgress(DeckBuildState State, BigInteger ResolvedSpace, BigInteger TotalSpace,
    BigInteger LegalDecksEvaluated, TimeSpan Elapsed, DeckOptimizationReport? Best,
    long? CompletedHands = null, long? TotalHands = null, long RequiredStarChips = 0);
public sealed record DeckProofResult(DeckBuildState State, bool ProvenOptimal, DeckOptimizationReport? Best,
    long RequiredStarChips, BigInteger ResolvedSpace, BigInteger TotalSpace, BigInteger LegalDecksEvaluated);

/// <summary>Proof-engine primitive. Cancellation durably pauses; the next invocation resumes.</summary>
public sealed class DeckProofSearch(FusionCatalog catalog)
{
    public const string RulesVersion = "concrete-fusion-terminal-equip-v1";
    private const int Schema = 2;
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();
    private readonly FusionCatalog _catalog = catalog;

    internal static DeckBuildRequest Freeze(DeckBuildRequest request) =>
        JsonSerializer.Deserialize<DeckBuildRequest>(JsonSerializer.Serialize(request, Json), Json)!;
    internal static string InputIdentity(DeckBuildRequest request) =>
        Hash(JsonSerializer.Serialize(request with { Mode = DeckSearchMode.Balanced }, Json));
    internal static string CatalogIdentity(FusionCatalog catalog, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return catalog.ContentIdentity;
    }

    public Task<DeckProofResult> RunAsync(DeckBuildRequest request, string checkpointPath,
        IProgress<DeckProofProgress>? progress = null, CancellationToken cancellationToken = default) =>
        RunWithIncumbentAsync(request, checkpointPath, null, progress, cancellationToken);

    // Only the job wrapper passes an incumbent, after validating its catalog/input identity,
    // exact statistics and legality. Preserve it even when the first notification pauses.
    internal Task<DeckProofResult> RunWithIncumbentAsync(DeckBuildRequest request, string checkpointPath,
        DeckBuildCandidate? incumbent, IProgress<DeckProofProgress>? progress, CancellationToken cancellationToken)
    {
        // Freeze before scheduling: caller edits cannot mutate the running request.
        var frozenJson = JsonSerializer.Serialize(request, Json);
        var frozen = JsonSerializer.Deserialize<DeckBuildRequest>(frozenJson, Json)!;
        return Task.Run(() => Run(frozen, frozenJson, checkpointPath, incumbent, progress, cancellationToken), CancellationToken.None);
    }

    private DeckProofResult Run(DeckBuildRequest request, string frozenJson, string path,
        DeckBuildCandidate? incumbent, IProgress<DeckProofProgress>? progress, CancellationToken token)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Fail closed if another process/job is already advancing this checkpoint.
        // The small lock file may remain after exit; only the open exclusive handle is the lease.
        using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var watch = Stopwatch.StartNew();
        progress?.Report(new(DeckBuildState.Preparing, 0, 0, 0, watch.Elapsed, null));
        // Identity covers actual cards/pairs/equips and scoring contexts, not just a filename.
        // Finish this short identity/checkpoint transaction even if Pause was requested at
        // the first Preparing notification. Nothing expensive is enumerated before it.
        var rulesHash = CatalogIdentity(_catalog, CancellationToken.None);
        var inputHash = Hash(frozenJson);
        var checkpoint = File.Exists(path) ? ReadCheckpoint(path) : new ProofCheckpoint(Schema, RulesVersion,
            DeckObjectiveComparer.Version, rulesHash, inputHash, frozenJson, "0", "0", null, 0, 0);
        if (checkpoint.SchemaVersion != Schema || checkpoint.RulesVersion != RulesVersion ||
            checkpoint.ObjectiveVersion != DeckObjectiveComparer.Version || checkpoint.CatalogHash != rulesHash ||
            checkpoint.RequestHash != inputHash || checkpoint.FrozenRequest != frozenJson)
            throw new InvalidDataException("Cannot resume: checkpoint rules, objective, catalog or frozen inputs differ. Start a new checkpoint.");
        var comparer = new DeckObjectiveComparer(request.Options.SafetyContext is not null, request.Options.SecondarySafetyContext is not null);
        var changedIncumbent = incumbent is not null && (checkpoint.Best is null || comparer.Compare(
            DeckObjectiveComparer.FromReport(incumbent.Report, incumbent.RequiredStarChips),
            DeckObjectiveComparer.FromReport(checkpoint.Best, checkpoint.BestSpend)) > 0);
        if (changedIncumbent)
            checkpoint = checkpoint with { Best = OptimizationReportSnapshot.Freeze(incumbent!.Report), BestSpend = incumbent.RequiredStarChips };
        var cursor = BigInteger.Parse(checkpoint.NextOrdinal, System.Globalization.CultureInfo.InvariantCulture);
        var legal = BigInteger.Parse(checkpoint.LegalDecks, System.Globalization.CultureInfo.InvariantCulture);
        if (!File.Exists(path) || changedIncumbent) WriteCheckpoint(path, checkpoint);
        DeckQuantitySpace space;
        try
        {
            token.ThrowIfCancellationRequested();
            space = new DeckQuantitySpace(_catalog, request.OwnedCards, request.Options, request.UseStarChips, request.StarChips, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            progress?.Report(new(DeckBuildState.Paused, cursor, 0, legal, watch.Elapsed,
                checkpoint.Best is null ? null : OptimizationReportSnapshot.Freeze(checkpoint.Best), RequiredStarChips: checkpoint.BestSpend));
            return new(DeckBuildState.Paused, false,
                checkpoint.Best is null ? null : OptimizationReportSnapshot.Freeze(checkpoint.Best), checkpoint.BestSpend, cursor, 0, legal);
        }
        if (cursor < 0 || cursor > space.CapacityVectorCount || legal < 0 || legal > cursor)
            throw new InvalidDataException("Checkpoint counters are invalid.");
        var best = checkpoint.Best is null ? null : OptimizationReportSnapshot.Freeze(checkpoint.Best);
        var spend = checkpoint.BestSpend;
        if (best is not null && (!best.ExactAnalysis.IsExact || best.ExactAnalysis.TotalHands != 658008 ||
            !space.IsLegal(Expand(best), out var required) || required != spend))
            throw new InvalidDataException("Checkpoint incumbent is not a legal exactly evaluated deck.");
        var optimizer = new OwnedDeckOptimizer(_catalog);
        var available = space.Capacities.Select(c => new OwnedCardQuantity(c.CardId, c.Capacity)).ToArray();
        var lastWrite = Stopwatch.StartNew();
        var lastProgress = Stopwatch.StartNew();
        TimeSpan Elapsed() => TimeSpan.FromMilliseconds(checkpoint.ElapsedMilliseconds) + watch.Elapsed;
        void Save()
        {
            WriteCheckpoint(path, checkpoint with
            {
                NextOrdinal = cursor.ToString(System.Globalization.CultureInfo.InvariantCulture),
                LegalDecks = legal.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Best = best,
                BestSpend = spend,
                ElapsedMilliseconds = Elapsed().TotalMilliseconds
            });
            lastWrite.Restart();
        }
        void Report(DeckBuildState state, long? done = null, long? total = null, bool force = false)
        {
            if (!force && lastProgress.ElapsedMilliseconds < 200) return;
            progress?.Report(new(state, cursor, space.CapacityVectorCount, legal, Elapsed(), best, done, total, spend));
            lastProgress.Restart();
        }
        try
        {
            Report(DeckBuildState.Searching, force: true);
            while (cursor < space.CapacityVectorCount)
            {
                token.ThrowIfCancellationRequested();
                var candidate = space.Resolve(cursor, token);
                if (candidate.Cards is not null)
                {
                    var report = optimizer.EvaluateCandidate(candidate.Cards, request.Options, exact: true,
                        new InlineProgress<DeckOptimizationProgress>(p => Report(DeckBuildState.Verifying, p.CompletedHands, p.TotalHands)), token);
                    if (best is null || comparer.Compare(DeckObjectiveComparer.FromReport(report, candidate.RequiredStarChips),
                            DeckObjectiveComparer.FromReport(best, spend)) > 0)
                    { best = OptimizationReportSnapshot.Freeze(optimizer.DescribeCandidate(report, available, request.Options)); spend = candidate.RequiredStarChips; }
                    legal++;
                }
                // Cursor advances only after successful complete evaluation, or sound branch pruning.
                cursor = candidate.NextOrdinal;
                Report(DeckBuildState.Searching);
                if (lastWrite.ElapsedMilliseconds >= 2000) Save();
            }
            Save();
            Report(DeckBuildState.Completed, force: true);
            return new(DeckBuildState.Completed, best is not null, best, spend, cursor, space.CapacityVectorCount, legal);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Report(DeckBuildState.Pausing, force: true);
            Save();
            Report(DeckBuildState.Paused, force: true);
            return new(DeckBuildState.Paused, false, best, spend, cursor, space.CapacityVectorCount, legal);
        }
        catch
        {
            // Previously completed work is durable; the failing leaf is never marked resolved.
            Save();
            Report(DeckBuildState.Failed, force: true);
            throw;
        }
    }

    private static int[] Expand(DeckOptimizationReport report) => [.. report.Deck.SelectMany(e => Enumerable.Repeat(e.Card.Id, e.Copies))];
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void WriteCheckpoint(string path, ProofCheckpoint checkpoint)
    {
        var payload = JsonSerializer.Serialize(checkpoint, Json);
        var envelope = JsonSerializer.SerializeToUtf8Bytes(new Envelope(Hash(payload), payload), Json);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".building";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(envelope); stream.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static ProofCheckpoint ReadCheckpoint(string path)
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Checkpoint is empty.");
        if (envelope.Checksum != Hash(envelope.Payload)) throw new InvalidDataException("Checkpoint checksum failed.");
        return JsonSerializer.Deserialize<ProofCheckpoint>(envelope.Payload, Json)
            ?? throw new InvalidDataException("Checkpoint payload is empty.");
    }
    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new ReadOnlyStringSetConverter());
        return options;
    }
    private sealed class ReadOnlyStringSetConverter : JsonConverter<IReadOnlySet<string>>
    {
        public override IReadOnlySet<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            (JsonSerializer.Deserialize<string[]>(ref reader, options) ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        public override void Write(Utf8JsonWriter writer, IReadOnlySet<string> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.Order(StringComparer.OrdinalIgnoreCase).ToArray(), options);
    }
    private sealed record Envelope(string Checksum, string Payload);
    private sealed record ProofCheckpoint(int SchemaVersion, string RulesVersion, string ObjectiveVersion,
        string CatalogHash, string RequestHash, string FrozenRequest, string NextOrdinal, string LegalDecks,
        DeckOptimizationReport? Best, long BestSpend, double ElapsedMilliseconds);
}
