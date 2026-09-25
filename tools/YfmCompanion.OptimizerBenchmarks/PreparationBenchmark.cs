#if !LEGACY_BASELINE
using System.Diagnostics;
using System.Text.Json;
using YfmCompanion.Data;
using YfmCompanion.Engine;

internal static class PreparationBenchmark
{
    internal static void Run(FusionCatalog catalog, string root, string output)
    {
        var research = CampaignResearchData.Load(Path.Combine(root, "docs", "research", "data"));
        var rows = new List<object>();
        foreach (var scope in Enum.GetValues<CampaignOpponentScope>())
        {
            var builder = new CampaignOptimizationContextBuilder(catalog, research);
            CampaignOptimizationContext? cold = null;
            for (var run = 0; run < 2; run++)
            {
                var clock = Stopwatch.StartNew();
                var times = new List<double> { 0 };
                var before = GC.GetTotalAllocatedBytes(true);
                var context = builder.Build(scope, 36, progress: new Inline<DeckOptimizationProgress>(_ => times.Add(clock.Elapsed.TotalMilliseconds)));
                times.Add(clock.Elapsed.TotalMilliseconds);
                if (cold is not null && !cold.OpponentThreatReports.SequenceEqual(context.OpponentThreatReports))
                    throw new InvalidOperationException("Warm preparation changed cached threat reports.");
                cold = context;
                var gap = times.Zip(times.Skip(1), (a, b) => b - a).Max();
                if (gap > 1000) throw new InvalidOperationException("Preparation progress silence exceeded one second.");
                rows.Add(new
                {
                    Scope = scope.ToString(),
                    Cache = run == 0 ? "cold" : "warm",
                    Milliseconds = clock.Elapsed.TotalMilliseconds,
                    MaximumProgressGapMilliseconds = gap,
                    AllocatedBytes = GC.GetTotalAllocatedBytes(true) - before,
                    Reports = context.OpponentThreatReports.Count
                });
            }
        }
        using var cancellation = new CancellationTokenSource();
        var cancelClock = Stopwatch.StartNew();
        double? requestedAt = null;
        try
        {
            new CampaignOptimizationContextBuilder(catalog, research).Build(CampaignOpponentScope.GeneralSafety,
                progress: new Inline<DeckOptimizationProgress>(_ => { requestedAt ??= cancelClock.Elapsed.TotalMilliseconds; cancellation.Cancel(); }),
                cancellationToken: cancellation.Token);
            throw new InvalidOperationException("Preparation ignored cancellation.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        var delay = cancelClock.Elapsed.TotalMilliseconds - requestedAt!.Value;
        if (delay > 1000) throw new InvalidOperationException("Preparation cancellation exceeded one second.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            CapturedUtc = DateTimeOffset.UtcNow,
            Environment.ProcessorCount,
            SyntheticOnly = true,
            Cases = rows,
            CancellationDelayMilliseconds = delay
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Preparation passed six cold/warm cases; cancellation {delay:F2} ms.");
    }
    private sealed class Inline<T>(Action<T> callback) : IProgress<T> { public void Report(T value) => callback(value); }
}
#endif
