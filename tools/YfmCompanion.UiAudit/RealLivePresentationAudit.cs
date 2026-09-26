using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using YfmCompanion.Desktop;
using YfmCompanion.RetroArch;
using static Program;

internal static class RealLivePresentationAudit
{
    internal static int Run(MainWindow window, string output)
    {
        using var client = new RetroArchNetworkClient();
        var reader = new ForbiddenMemoriesLiveReader(client);
        var clock = Stopwatch.StartNew();
        var polls = new List<object>();
        var failures = new List<string>();
        var successful = 0;
        var playing = 0;
        var changes = 0;
        var selectionChecks = 0;
        var changedSelectionChecks = 0;
        var transientReads = 0;
        var transientSelectionChecks = 0;
        double maxPresentation = 0;
        double maximumOffset = 0;
        ForbiddenMemoriesLiveSnapshot? previous = null;
        ((TabControl)window.FindName("WorkspaceTabs")).SelectedIndex = 2;
        var pane = (UserControl)window.FindName("LiveDuelPaneView");
        ((Grid)pane.Content).Children.OfType<TabControl>().Single().SelectedIndex = 1;
        var grid = (DataGrid)window.FindName("LiveDeckGrid");
        window.Width = 1100;
        window.Height = 600;
        object? items = null;
        var expectedSelection = -1;
        double expectedOffset = 0;
        ScrollViewer? scroll = null;

        // One sequential, bounded read per second. Never send game inputs or writes.
        while (clock.Elapsed < TimeSpan.FromSeconds(60))
        {
            var started = clock.Elapsed.TotalMilliseconds;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                var read = reader.ReadSnapshotAsync(timeout.Token);
                Await(read);
                var snapshot = read.GetAwaiter().GetResult();
                var changed = snapshot.DuelActive && previous is { DuelActive: true } prior &&
                    (!snapshot.HandCardIds.SequenceEqual(prior.HandCardIds) ||
                     !snapshot.PlayerField.SequenceEqual(prior.PlayerField) ||
                     !snapshot.PlayerSpellTrapField.SequenceEqual(prior.PlayerSpellTrapField) ||
                     !snapshot.OpponentField.SequenceEqual(prior.OpponentField) ||
                     snapshot.PlayerLifePoints != prior.PlayerLifePoints ||
                     snapshot.OpponentLifePoints != prior.OpponentLifePoints ||
                     snapshot.TerrainId != prior.TerrainId);
                if (changed) changes++;
                if (snapshot.DuelActive && snapshot.Status.State == RetroArchPlaybackState.Playing) playing++;
                var sameDeckShape = previous is not null &&
                    snapshot.ConstructedDeckCardIds.Count == previous.ConstructedDeckCardIds.Count;
                var renderClock = Stopwatch.StartNew();
                Invoke(window, "ShowLiveSnapshot", snapshot);
                Pump(TimeSpan.FromMilliseconds(30));
                maxPresentation = Math.Max(maxPresentation, renderClock.Elapsed.TotalMilliseconds);
                if (items is not null && sameDeckShape && expectedSelection < grid.Items.Count)
                {
                    if (!ReferenceEquals(items, grid.ItemsSource) || grid.SelectedIndex != expectedSelection ||
                        Math.Abs(scroll!.VerticalOffset - expectedOffset) > .1)
                        throw new InvalidOperationException("Actual live snapshot reset deck selection or scroll.");
                    selectionChecks++;
                    if (changed) changedSelectionChecks++;
                }
                else if (grid.Items.Count > 20)
                {
                    grid.BringIntoView();
                    grid.SelectedIndex = 20;
                    grid.ScrollIntoView(grid.SelectedItem);
                    Pump(TimeSpan.FromMilliseconds(50));
                    scroll = Descendants(grid).OfType<ScrollViewer>().First();
                    scroll.ScrollToBottom();
                    Pump(TimeSpan.FromMilliseconds(50));
                    items = grid.ItemsSource;
                    expectedSelection = grid.SelectedIndex;
                    expectedOffset = scroll.VerticalOffset;
                    maximumOffset = Math.Max(maximumOffset, expectedOffset);
                }
                else
                {
                    items = null;
                }
                previous = snapshot;
                successful++;
                polls.Add(new
                {
                    StartedMilliseconds = started,
                    DurationMilliseconds = clock.Elapsed.TotalMilliseconds - started,
                    snapshot.DuelActive,
                    Playback = snapshot.Status.State.ToString(),
                    Changed = changed
                });
            }
            catch (RetroArchTransientStateException error)
            {
                transientReads++;
                Invoke(window, "SetLiveTransient", error.Message);
                Pump(TimeSpan.FromMilliseconds(30));
                if (items is not null)
                {
                    if (!ReferenceEquals(items, grid.ItemsSource) || grid.SelectedIndex != expectedSelection ||
                        Math.Abs(scroll!.VerticalOffset - expectedOffset) > .1)
                        failures.Add("Transient read reset collection selection/scroll.");
                    else transientSelectionChecks++;
                }
                if (((ListBox)window.FindName("LiveHandGrid")).Items.Count != 0 ||
                    ((DataGrid)window.FindName("LiveAdviceGrid")).Items.Count != 0)
                    failures.Add("Transient read retained unvalidated live hand/advice.");
                polls.Add(new { StartedMilliseconds = started, Transient = true });
            }
            catch (Exception error)
            {
                failures.Add(error is InvalidOperationException ? error.Message : error.GetType().Name);
                previous = null;
                items = null;
            }
            var remaining = 1000 - (clock.Elapsed.TotalMilliseconds - started);
            if (remaining > 0) Pump(TimeSpan.FromMilliseconds(remaining));
        }
        var passed = failures.Count == 0 && playing >= 10 && changes >= 2 &&
            selectionChecks >= 10 && changedSelectionChecks >= 2 && maximumOffset > 0;
        var result = new
        {
            SchemaVersion = 1,
            CapturedUtc = DateTimeOffset.UtcNow,
            Passed = passed,
            Privacy = "Only timing/status/change counts and assertion failures; no screenshots, cards, personal paths, saves or raw memory.",
            Scope = "Actual read-only RetroArch snapshots through production WPF ShowLiveSnapshot; selected constructed-deck row and nonzero internal scroll retained. Optimizer coexistence measured separately.",
            SuccessfulReads = successful,
            PlayingReads = playing,
            DuelStateChanges = changes,
            SelectionAndScrollChecks = selectionChecks,
            ChangedSnapshotSelectionAndScrollChecks = changedSelectionChecks,
            TransientReads = transientReads,
            TransientSelectionAndScrollChecks = transientSelectionChecks,
            MaximumScrollOffset = maximumOffset,
            MaximumPresentationMillisecondsIncludingPump = maxPresentation,
            Failures = failures,
            Polls = polls
        };
        File.WriteAllText(Path.Combine(output, "real-live-presentation.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Real live UI: passed={passed}; reads={successful}; playing={playing}; changes={changes}; preserved={selectionChecks}; changed-preserved={changedSelectionChecks}; failures={failures.Count}.");
        window.Close();
        return passed ? 0 : 1;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
        {
            var child = VisualTreeHelper.GetChild(node, index);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
