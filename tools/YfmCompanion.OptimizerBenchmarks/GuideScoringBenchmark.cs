using System.Diagnostics;
using System.Text.Json;
using YfmCompanion.Data;
using YfmCompanion.Engine;

internal static class GuideScoringBenchmark
{
    public static async Task RunAsync(FusionCatalog catalog, string output)
    {
        var materials = catalog.Cards.Where(c => c.Attack is > 0 and < 1800 &&
            c.PrimaryType is "Dragon" or "Thunder" or "Rock" or "Plant").OrderBy(c => c.Id).Take(28).ToArray();
        var original = materials.Take(14).SelectMany(c => Enumerable.Repeat(c.Id, 3)).Take(40).ToArray();
        var supportIds = new[] { "Umi", "Dragon Treasure", "Invigoration", "Raigeki" }.Select(n => catalog.GetCard(n).Id).ToArray();
        var supported = original.Take(36).Concat(supportIds).ToArray();
        var owned = materials.Select(c => new OwnedCardQuantity(c.Id, 3))
            .Concat(supportIds.Select(id => new OwnedCardQuantity(id, id == 337 ? 1 : 3)))
            .Append(new OwnedCardQuantity(336, 1)).Append(new OwnedCardQuantity(686, 2)).ToArray();
        var optimizer = new OwnedDeckOptimizer(catalog, DeckBuildJob.AutoAnalysisWorkerCount);
        var options = new DeckOptimizationOptions(IncludeGlitches: false);
        var clock = Stopwatch.StartNew();
        var plain = optimizer.EvaluateDeck(original, owned, options);
        var package = optimizer.EvaluateDeck(supported, owned, options);
        clock.Stop();
        var recovery = optimizer.EvaluateDeck(original.Take(39).Append(337), owned, options);
        var comparer = new DeckObjectiveComparer(false, false);
        var before = comparer.Compare(DeckObjectiveComparer.FromReport(package) with { UseGuideSupport = false },
            DeckObjectiveComparer.FromReport(plain) with { UseGuideSupport = false });
        var after = comparer.Compare(DeckObjectiveComparer.FromReport(package), DeckObjectiveComparer.FromReport(plain));
        var job = new DeckBuildJob(catalog, new(owned, options, DeckSearchMode.Quick));
        var timed = await job.RunAsync();
        var exactClock = Stopwatch.StartNew();
        var verified = await job.VerifyBestAsync();
        exactClock.Stop();
        if (verified.State != DeckBuildState.Completed || verified.Best!.Report.ExactAnalysis.TotalHands != 658008 || verified.ProvenOptimal)
            throw new InvalidDataException("Guide-scored deck did not verify correctly.");
        var report = verified.Best.Report;
        if (report.Deck.Sum(e => e.Copies) != 40 || report.Deck.Any(e => e.Copies > 3) ||
            verified.SearchCache!.AccountedBytes > verified.SearchCache.LimitBytes) throw new InvalidDataException("Legality/cache check failed.");
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            ObjectiveVersion = DeckObjectiveComparer.Version,
            RulesVersion = DeckProofSearch.RulesVersion,
            job.SearchWorkerCount,
            AnalysisWorkers = job.WorkerCount,
            CompareBefore = before,
            CompareAfter = after,
            RecoveryBefore = comparer.Compare(DeckObjectiveComparer.FromReport(recovery) with { UseGuideSupport = false }, DeckObjectiveComparer.FromReport(plain) with { UseGuideSupport = false }),
            RecoveryAfter = comparer.Compare(DeckObjectiveComparer.FromReport(recovery), DeckObjectiveComparer.FromReport(plain)),
            Recovery = recovery,
            FixtureExactMilliseconds = clock.Elapsed.TotalMilliseconds,
            Plain = plain,
            Supported = package,
            TimedCandidates = timed.CandidatesExamined,
            ExactMilliseconds = exactClock.Elapsed.TotalMilliseconds,
            Verified = verified,
            BestFoundDeck = report.Deck.Select(e => new { e.Card.Name, e.Copies }),
            owned
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Old ranking: {before}; guide ranking: {after}; {timed.CandidatesExamined} candidates; exact {exactClock.Elapsed.TotalMilliseconds:F0} ms; setup {report.ExactAnalysis.Setup3500Probability:P1}; clear {report.ExactAnalysis.BoardClearProbability:P1}");
        foreach (var e in report.Deck) Console.WriteLine($"{e.Copies} x {e.Card.Name}");
    }
}
