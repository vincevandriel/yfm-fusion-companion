using System.Diagnostics;
using System.Text.Json;
using YfmCompanion.Data;
using YfmCompanion.Engine;

// Deliberately uses the v1.1 public API so the identical harness can measure both trees.
var root = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var matrix = args.Contains("--matrix", StringComparer.Ordinal);
var seconds = args.Contains("--short", StringComparer.Ordinal) ? 2 : 30;
var dbDirectory = Path.Combine(Path.GetTempPath(), "yfm-benchmark", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dbDirectory);
var db = Path.Combine(dbDirectory, "synthetic-benchmark-catalog.db");
PostgresDumpImporter.Import(Path.Combine(root, "database-source", "YuGiOh_Forbidden_Memories_PostgreSQL.sql"), db);
var catalog = FusionCatalog.Load(db);
var low = catalog.Cards.Where(c => c.Attack is > 0 and < 1400 && c.Id is not (>= 17 and <= 21)).OrderBy(c => c.Id).ToArray();
var fixtures = new Dictionary<string, OwnedCardQuantity[]>
{
    ["starter-duplicates"] = low.Take(14).Select(c => new OwnedCardQuantity(c.Id, 3)).ToArray(),
    ["starter-diverse"] = low.Take(42).Select(c => new OwnedCardQuantity(c.Id, 1)).ToArray(),
    ["medium"] = catalog.Cards.OrderBy(c => c.Id).Take(100).Select(c => new OwnedCardQuantity(c.Id, 3)).ToArray(),
    ["near-complete"] = catalog.Cards.OrderBy(c => c.Id).Select(c => new OwnedCardQuantity(c.Id, 3)).ToArray()
};
#if !LEGACY_BASELINE
if (args.Contains("--campaign-library", StringComparer.Ordinal))
{
    await CampaignLibraryBenchmark.RunAsync(catalog, output);
    return;
}
if (args.Contains("--guides", StringComparer.Ordinal))
{
    await GuideScoringBenchmark.RunAsync(catalog, output);
    return;
}
if (args.Contains("--parallel", StringComparer.Ordinal))
{
    await ParallelismBenchmark.RunAsync(catalog, root, output);
    return;
}
if (args.Contains("--prepare", StringComparer.Ordinal))
{
    PreparationBenchmark.Run(catalog, root, output);
    return;
}
if (args.Contains("--live", StringComparer.Ordinal))
{
    await LiveOptimizerBenchmark.RunAsync(catalog, fixtures["near-complete"], output,
        args.Contains("--quick", StringComparer.Ordinal) ? DeckSearchMode.Quick :
        args.Contains("--thorough", StringComparer.Ordinal) ? DeckSearchMode.Thorough : DeckSearchMode.Balanced);
    return;
}
#endif
var results = new List<object>();
var contexts = matrix ? new[] { "manual", "specific", "general" } : ["manual"];
foreach (var fixture in fixtures)
    foreach (var contextName in contexts)
        foreach (var chips in matrix ? new[] { false, true } : [false])
        {
            // Same planner instance makes the second run a genuine warm-cache measurement.
            var planner = new StarChipDeckPlanner(catalog);
            var options = new DeckOptimizationOptions(SampleHands: 160, ExactFinalists: 2, IncludeGlitches: false);
            if (contextName != "manual")
            {
                var research = CampaignResearchData.Load(Path.Combine(root, "docs", "research", "data"));
                var builder = new CampaignOptimizationContextBuilder(catalog, research);
                options = options with
                {
                    Profile = DeckStrategyProfile.ControlAndSafety,
                    SafetyContext = builder.Build(contextName == "general" ? CampaignOpponentScope.GeneralSafety : CampaignOpponentScope.SpecificOpponent, 1).Safety,
                    SecondarySafetyContext = contextName == "general" ? builder.Build(CampaignOpponentScope.FinalGauntlet).Safety : null
                };
            }
            for (var repetition = 0; repetition < 2; repetition++)
            {
                var watch = Stopwatch.StartNew();
                var ticks = new List<(string Stage, double Time)>();
                var progress = new InlineProgress<DeckOptimizationProgress>(p => ticks.Add((p.Stage, watch.Elapsed.TotalMilliseconds)));
                var allocatedBefore = GC.GetTotalAllocatedBytes(true);
                using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
                var status = "completed";
                StarChipDeckPlan? result = null;
                try { result = planner.Plan(fixture.Value, 500, chips, options, progress: progress, cancellationToken: cancel.Token); }
                catch (OperationCanceledException) { status = "cancelled-at-budget"; }
                watch.Stop();
                var allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
                using var process = Process.GetCurrentProcess();
                var entry = new
                {
                    Fixture = fixture.Key,
                    Context = contextName,
                    Chips = chips,
                    Warm = repetition != 0,
                    Status = status,
                    ElapsedMs = watch.Elapsed.TotalMilliseconds,
                    AllocatedBytes = allocated,
                    ProcessLifetimePeakWorkingSetBytes = process.PeakWorkingSet64,
                    CancellationOverrunMs = status == "completed" ? (double?)null : Math.Max(0, watch.Elapsed.TotalMilliseconds - seconds * 1000),
                    FirstUsableDeckMs = result is null ? (double?)null : watch.Elapsed.TotalMilliseconds,
                    FirstResultLimitation = "Legacy API exposes a usable deck only on return; no incremental result callback.",
                    ProgressEvents = ticks.Count,
                    MaximumProgressGapMs = ticks.Select(t => t.Time).Prepend(0).Append(watch.Elapsed.TotalMilliseconds).Zip(ticks.Select(t => t.Time).Append(watch.Elapsed.TotalMilliseconds), (a, b) => b - a).DefaultIfEmpty().Max(),
                    StageEvents = ticks.Select(t => new { t.Stage, Milliseconds = t.Time }).ToArray(),
                    Deck = result?.ResultingDeck.Deck.Select(e => new { Id = e.Card.Id, e.Copies }),
                    Hands = result?.ResultingDeck.ExactAnalysis.TotalHands
                };
                results.Add(entry);
                if (!args.Contains("--quiet", StringComparer.Ordinal)) Console.WriteLine(JsonSerializer.Serialize(entry));
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.WriteAllText(output, JsonSerializer.Serialize(new
                {
                    SchemaVersion = 1,
                    CapturedUtc = DateTimeOffset.UtcNow,
                    LogicalProcessors = Environment.ProcessorCount,
                    Runtime = Environment.Version.ToString(),
                    OS = Environment.OSVersion.ToString(),
                    PerCaseCancellationSeconds = seconds,
                    LiveReaderCoverage = "Unverified: no live-reader benchmark in this harness.",
                    Fixtures = fixtures,
                    Results = results
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
        }

sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}
