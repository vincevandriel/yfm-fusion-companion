using System.Diagnostics;
using System.Text.Json;
using YfmCompanion.Data;
using YfmCompanion.Engine;

internal static class CampaignLibraryBenchmark
{
    public static async Task RunAsync(FusionCatalog catalog, string output)
    {
        var builds = CampaignDeckLibrary.ForCatalog(catalog);
        var reports = new List<object>();
        foreach (var build in builds)
        {
            var clock = Stopwatch.StartNew();
            var serial = new DeckAnalyzer(catalog).Analyze(build.Expand(), false);
            var serialMs = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            var parallel = new DeckAnalyzer(catalog, workerCount: DeckBuildJob.AutoAnalysisWorkerCount).Analyze(build.Expand(), false);
            var parallelMs = clock.Elapsed.TotalMilliseconds;
            if (serial.TotalHands != 658008 || JsonSerializer.Serialize(serial) != JsonSerializer.Serialize(parallel))
                throw new InvalidDataException($"Exact serial/parallel mismatch for {build.Name}.");
            var structure = GuideSupportStructure.Evaluate(catalog, build.Expand(), serial);
            var r = serial;
            var oldScore = (40.0 * r.HandsWith3500SetupOrBoardClear + 25.0 * r.HandsAtLeast2800 +
                15.0 * r.HandsWithBroadRemoval + 10.0 * r.HandsWith3500Setup + 5.0 * r.HandsWith4500Setup +
                5.0 * r.HandsWithBoardClear) / r.TotalHands + structure.Points;
            var newScore = (35.0 * r.HandsWith3500SetupOrBoardClear + 20.0 * r.HandsWith2800Body +
                15.0 * r.HandsWithEndgamePowerOrBoardClear + 10.0 * r.HandsWithBroadRemoval +
                10.0 * r.HandsWith3500Setup + 5.0 * r.HandsWithEndgamePower + 5.0 * r.HandsWithBoardClear -
                15.0 * r.HandsWithNoMonster) / r.TotalHands + structure.Points;
            reports.Add(new { build.Id, build.Name, SerialMilliseconds = serialMs, ParallelMilliseconds = parallelMs,
                ExactSerialParallelIdentical = true, OldHeuristicScore = oldScore, NewHeuristicScore = newScore,
                structure, Analysis = serial });
            Console.WriteLine($"{build.Name}: body {r.Body2800Probability:P1}, >4500 {r.EndgamePowerProbability:P1}, clear {r.BoardClearProbability:P1}, no monster {r.NoMonsterProbability:P1}; exact 1/{DeckBuildJob.AutoAnalysisWorkerCount} workers identical.");
        }
        var owned = builds.SelectMany(b => b.Entries).GroupBy(e => e.CardId).Select(g => new OwnedCardQuantity(g.Key, g.Max(e => e.Copies))).ToArray();
        var job = new DeckBuildJob(catalog, new(owned, new(IncludeGlitches: false, RecommendedBuildId: "sand-mercury"), DeckSearchMode.Quick));
        var stages = new HashSet<string>();
        var result = await job.RunAsync(new InlineProgress(p => stages.Add(p.Stage)));
        var verified = await job.VerifyBestAsync();
        if (verified.Best?.Report.ExactAnalysis.TotalHands != 658008 || verified.ProvenOptimal || verified.Best.Report.TotalCards != 40)
            throw new InvalidDataException("Adapted search failed exact verification.");
        var capacities = owned.ToDictionary(e => e.CardId, e => e.Quantity);
        if (verified.Best.Report.Deck.Any(e => e.Copies > capacities.GetValueOrDefault(e.Card.Id)))
            throw new InvalidDataException("Search used missing cards.");
        File.WriteAllText(output, JsonSerializer.Serialize(new { CampaignDeckLibrary.Version, ObjectiveVersion = DeckObjectiveComparer.Version,
            RulesVersion = DeckProofSearch.RulesVersion, CatalogIdentity = catalog.ContentIdentity, References = reports,
            job.SearchWorkerCount, AnalysisWorkers = job.WorkerCount, TimedCandidates = result.CandidatesExamined,
            SearchStages = stages.Order().ToArray(), BestFound = verified, WinRateMeasured = false,
            Scope = "Vanilla opening-hand availability; multi-turn setups, no opponent-response or full-duel simulation." }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class InlineProgress(Action<DeckBuildProgress> report) : IProgress<DeckBuildProgress>
    { public void Report(DeckBuildProgress value) => report(value); }
}
