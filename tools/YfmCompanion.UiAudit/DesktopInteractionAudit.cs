using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using YfmCompanion.Desktop;
using YfmCompanion.Engine;
using YfmCompanion.RetroArch;
using static Program;

internal static class DesktopInteractionAudit
{
    internal static void Run(MainWindow window)
    {
        var tabs = (TabControl)window.FindName("WorkspaceTabs");
        tabs.SelectedIndex = 4;
        Invoke(window, "SetOptimizerRunning", true);
        var gallery = (ListBox)window.FindName("OwnedCardsGallery");
        gallery.BringIntoView();
        Pump(TimeSpan.FromMilliseconds(50));
        var editor = Descendants(gallery).OfType<TextBox>().FirstOrDefault();
        if (!gallery.IsEnabled || editor is null || !editor.IsReadOnly)
            throw new InvalidOperationException("An active build must allow gallery browsing but prevent quantity edits.");
        var dock = (FrameworkElement)window.FindName("OptimizerActivityDock");
        var scroll = (ScrollViewer)window.FindName("FullWorkspaceScroll");
        var position = dock.TranslatePoint(new Point(), window);
        scroll.ScrollToBottom();
        Pump(TimeSpan.FromMilliseconds(50));
        if (!dock.IsVisible || Math.Abs(position.Y - dock.TranslatePoint(new Point(), window).Y) > 1)
            throw new InvalidOperationException("Optimizer activity scrolled away.");
        Invoke(window, "SetOptimizerRunning", false);
        tabs.SelectedIndex = 2;
        var deck = Enumerable.Range(1, 40).ToArray();
        var snapshot = new ForbiddenMemoriesLiveSnapshot(new(RetroArchPlaybackState.Playing, "PSX", "Synthetic", null, "synthetic"),
            true, [2, 8, 1, 2, 3], [new(1, 1, 3000, 2500)], [], [new(2, 22, 2500, 1200)], [],
            0, deck, deck, new int[722], true, 8000, 8000, 0, DateTimeOffset.UtcNow, "SYNTHETIC");
        Invoke(window, "ShowLiveSnapshot", snapshot);
        var grid = (DataGrid)window.FindName("LiveDeckGrid");
        var liveView = (UserControl)window.FindName("LiveDuelPaneView");
        ((Grid)liveView.Content).Children.OfType<TabControl>().Single().SelectedIndex = 1;
        grid.BringIntoView();
        Pump(TimeSpan.FromMilliseconds(50));
        grid.SelectedIndex = 20;
        grid.ScrollIntoView(grid.SelectedItem);
        Pump(TimeSpan.FromMilliseconds(50));
        var internalScroll = Descendants(grid).OfType<ScrollViewer>().First();
        var offset = internalScroll.VerticalOffset;
        var items = grid.ItemsSource;
        for (var frame = 0; frame < 20; frame++)
        {
            var changed = deck.ToArray();
            changed[20] = frame % 2 == 0 ? 22 : 23;
            Invoke(window, "ShowLiveSnapshot", snapshot with
            {
                ConstructedDeckCardIds = changed,
                PlayerField = frame % 2 == 0 ? [new(1, 1, 3500, 3000)] : [new(3, 22, 2500, 1200)],
                PlayerLifePoints = 8000 - frame * 100
            });
            Pump(TimeSpan.FromMilliseconds(10));
            if (!ReferenceEquals(items, grid.ItemsSource) || grid.SelectedIndex != 20 ||
                Math.Abs(internalScroll.VerticalOffset - offset) > .1)
                throw new InvalidOperationException("Changing synthetic live snapshots reset selection/scroll.");
        }
        Invoke(window, "SetLiveUnavailable", "Offline fixture", "Synthetic disconnect", "ERROR", "#7B3B45");
        if (grid.Items.Count != 0) throw new InvalidOperationException("Disconnect left stale live rows.");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
