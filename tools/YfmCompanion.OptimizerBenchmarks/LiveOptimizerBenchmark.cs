using System.Diagnostics;
using System.Text.Json;
using YfmCompanion.Data;
using YfmCompanion.Engine;
using YfmCompanion.RetroArch;

internal static class LiveOptimizerBenchmark
{
    public static async Task RunAsync(FusionCatalog catalog, OwnedCardQuantity[] syntheticInventory, string output,
        DeckSearchMode mode = DeckSearchMode.Balanced)
    {
        using var client = new RetroArchNetworkClient();
        var reader = new ForbiddenMemoriesLiveReader(client);
        // Fail before any optimizer load if the read-only connection is unavailable.
        _ = await reader.ReadSnapshotAsync();
        using var stop = new CancellationTokenSource();
        var samples = new List<Poll>();
        var progress = new List<ProgressSample>();
        var phase = "idle";
        var active = 0;
        var maximumActive = 0;
        var duelChanges = 0;
        ForbiddenMemoriesLiveSnapshot? previousDuel = null;
        var watch = Stopwatch.StartNew();
        var allocationsBefore = GC.GetTotalAllocatedBytes(true);
        var pollTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                while (await timer.WaitForNextTickAsync(stop.Token))
                {
                    var started = watch.Elapsed.TotalMilliseconds;
                    var phaseAtStart = phase;
                    maximumActive = Math.Max(maximumActive, Interlocked.Increment(ref active));
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(2));
                    try
                    {
                        var snapshot = await reader.ReadSnapshotAsync(timeout.Token);
                        if (snapshot.DuelActive && previousDuel is { DuelActive: true } previous &&
                            (!snapshot.HandCardIds.SequenceEqual(previous.HandCardIds) ||
                             !snapshot.PlayerField.SequenceEqual(previous.PlayerField) ||
                             !snapshot.PlayerSpellTrapField.SequenceEqual(previous.PlayerSpellTrapField) ||
                             !snapshot.OpponentField.SequenceEqual(previous.OpponentField) ||
                             snapshot.PlayerLifePoints != previous.PlayerLifePoints ||
                             snapshot.OpponentLifePoints != previous.OpponentLifePoints ||
                             snapshot.TerrainId != previous.TerrainId)) duelChanges++;
                        previousDuel = snapshot;
                        samples.Add(new(phaseAtStart, started, watch.Elapsed.TotalMilliseconds - started,
                            true, snapshot.DuelActive, snapshot.Status.State.ToString(), null));
                    }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                    catch (Exception error)
                    {
                        samples.Add(new(phaseAtStart, started, watch.Elapsed.TotalMilliseconds - started,
                            false, false, null, error.GetType().Name));
                    }
                    finally { Interlocked.Decrement(ref active); }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        });
        DeckBuildResult? result = null;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            phase = "optimizer";
            var start = watch.Elapsed.TotalMilliseconds;
            var job = new DeckBuildJob(catalog, new(syntheticInventory, new(IncludeGlitches: false), mode));
            result = await job.RunAsync(new DirectProgress<DeckBuildProgress>(p => progress.Add(new(
                watch.Elapsed.TotalMilliseconds - start, p.State.ToString(), p.Best is not null, p.CompletedHands, p.TotalHands,
                p.Stage, p.SearchTimeConsumed.TotalMilliseconds, p.CandidatesExamined))));
            phase = "post-optimizer";
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
        finally
        {
            stop.Cancel();
            await pollTask;
            var data = new
            {
                SchemaVersion = 2,
                CapturedUtc = DateTimeOffset.UtcNow,
                LogicalProcessors = Environment.ProcessorCount,
                Privacy = "Only timing, status and change counts; no game cards, save contents, paths or raw memory.",
                Inventory = "Synthetic near-complete collection",
                Mode = mode.ToString(),
                PollIntervalMilliseconds = 1000,
                MaximumConcurrentReads = maximumActive,
                SuccessfulReads = samples.Count(p => p.Success),
                FailedReads = samples.Count(p => !p.Success),
                ActiveDuelReads = samples.Count(p => p.DuelActive),
                PlayingReads = samples.Count(p => p.DuelActive && p.Playback == "Playing"),
                DuelStateChanges = duelChanges,
                FirstUsableDeckMilliseconds = progress.FirstOrDefault(p => p.HasBest)?.Milliseconds,
                FirstProgressMilliseconds = progress.FirstOrDefault()?.Milliseconds,
                MaximumProgressGapMilliseconds = progress.Zip(progress.Skip(1), (a, b) => b.Milliseconds - a.Milliseconds).DefaultIfEmpty().Max(),
                ResultState = result?.State.ToString(),
                ExactHands = result?.Best?.Report.ExactAnalysis.TotalHands,
                ResultStatisticsExact = result?.Best?.Report.ExactAnalysis.IsExact,
                result?.ProvenOptimal,
                result?.CandidatesExamined,
                result?.SearchCache,
                AllocatedBytesIncludingLivePolling = GC.GetTotalAllocatedBytes(true) - allocationsBefore,
                ProcessLifetimePeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
                Polls = samples,
                Progress = progress
            };
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Live benchmark: {samples.Count(p => p.Success)} successful, {samples.Count(p => !p.Success)} failed reads; maximum concurrent reads {maximumActive}; result {result?.State}.");
        }
    }

    private sealed record Poll(string Phase, double StartedMilliseconds, double DurationMilliseconds, bool Success, bool DuelActive, string? Playback, string? ErrorType);
    private sealed record ProgressSample(double Milliseconds, string State, bool HasBest, long? CompletedHands, long? TotalHands,
        string Stage, double SearchConsumedMilliseconds, long CandidatesExamined);
    private sealed class DirectProgress<T>(Action<T> callback) : IProgress<T> { public void Report(T value) => callback(value); }
}
