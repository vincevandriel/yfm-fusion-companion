using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using YfmCompanion.Data;
using YfmCompanion.Engine;

internal static class ParallelismBenchmark
{
    public static async Task RunAsync(FusionCatalog catalog, string root, string output)
    {
        var low = catalog.Cards.Where(c => c.Attack is > 0 and < 1400 && c.Id is not (>= 17 and <= 21)).OrderBy(c => c.Id).ToArray();
        var decks = new Dictionary<string, int[]>
        {
            ["duplicates"] = low.Take(14).SelectMany(c => Enumerable.Repeat(c.Id, 3)).Take(40).ToArray(),
            ["diverse"] = low.Take(40).Select(c => c.Id).ToArray()
        };
        var measurements = new List<object>();
        var counts = new[] { 1, 2, DeckBuildJob.MaximumWorkerCount }.Distinct().ToArray();
        // Warm JIT only. Every measured analyzer is fresh and has the same aggregate budget.
        _ = new DeckAnalyzer(catalog, 0).Analyze(decks["duplicates"].Take(8));
        foreach (var fixture in decks)
        {
            string? baseline = null;
            foreach (var workers in counts)
                for (var repeat = 0; repeat < 3; repeat++)
                {
                    var analyzer = new DeckAnalyzer(catalog, workerCount: workers);
                    var cpu = Process.GetCurrentProcess().TotalProcessorTime;
                    var clock = Stopwatch.StartNew();
                    var report = analyzer.Analyze(fixture.Value, false);
                    clock.Stop();
                    var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(report)));
                    baseline ??= hash;
                    if (hash != baseline || report.TotalHands != 658008) throw new InvalidDataException("Parallel results differ from serial baseline.");
                    measurements.Add(new
                    {
                        Fixture = fixture.Key,
                        Workers = workers,
                        Repeat = repeat,
                        Milliseconds = clock.Elapsed.TotalMilliseconds,
                        CpuMilliseconds = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds,
                        ReportHash = hash,
                        report.TotalHands,
                        Cache = analyzer.CacheDiagnostics
                    });
                    Console.WriteLine($"{fixture.Key}: {workers} workers, {clock.Elapsed.TotalMilliseconds:F0} ms, identical report");
                }
        }
        var search = new List<object>();
        var owned = low.Take(80).Select(c => new OwnedCardQuantity(c.Id, 3)).ToArray();
        var research = CampaignResearchData.Load(Path.Combine(root, "docs", "research", "data"));
        var context = new CampaignOptimizationContextBuilder(catalog, research).Build(CampaignOpponentScope.GeneralSafety).Safety;
        var searchOptions = new Dictionary<string, DeckOptimizationOptions>
        {
            ["fusion"] = new(IncludeGlitches: false),
            ["campaign"] = new(IncludeGlitches: false, Profile: DeckStrategyProfile.ControlAndSafety, SafetyContext: context)
        };
        foreach (var settings in searchOptions)
            foreach (var workers in counts)
                for (var repeat = 0; repeat < 2; repeat++)
                {
                    var job = new DeckBuildJob(catalog, new(owned, settings.Value, DeckSearchMode.Quick), workerCount: workers);
                    var clock = Stopwatch.StartNew();
                    var result = await job.RunAsync();
                    clock.Stop();
                    if (result.State != DeckBuildState.Completed || result.Best?.Report.Deck.Sum(e => e.Copies) != 40)
                        throw new InvalidDataException("Search did not complete with a legal deck.");
                    search.Add(new
                    {
                        Fixture = settings.Key,
                        Workers = workers,
                        Repeat = repeat,
                        Milliseconds = clock.Elapsed.TotalMilliseconds,
                        result.CandidatesExamined,
                        result.SearchCache,
                        result.ProvenOptimal,
                        result.Best.Report.ExactAnalysis.IsExact
                    });
                    Console.WriteLine($"{settings.Key} search: {workers} workers, {result.CandidatesExamined} candidates in {clock.Elapsed.TotalSeconds:F2}s");
                }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            LogicalProcessors = Environment.ProcessorCount,
            ExactAnalysis = measurements,
            TimedSearch = search
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
