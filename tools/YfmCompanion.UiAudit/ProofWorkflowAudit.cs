using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using YfmCompanion.Desktop;
using YfmCompanion.Desktop.Views;
using YfmCompanion.Engine;
using static Program;

internal static class ProofWorkflowAudit
{
    internal static void Run()
    {
        var original = DesktopSettingsStore.Load();
        MainWindow? window = null;
        try
        {
            window = Open();
            Invoke(window, "ClearOwned_Click", window, new RoutedEventArgs());
            var rows = (List<OwnedCardRow>)Field(window, "_ownedCardRows")!;
            for (var i = 0; i < 14; i++) rows[i].Quantity = i == 13 ? 1 : 3;
            ((ComboBox)window.FindName("CampaignScopeCombo")).SelectedIndex = 0;
            var build = (Button)window.FindName("OptimizeDeckButton");
            build.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Button)window.FindName("DockPauseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitUntil(() => build.IsEnabled, TimeSpan.FromSeconds(1), "Preparation pause");
            if (Field(window, "_preparationPaused") is not true ||
                ((FrameworkElement)window.FindName("OptimizerSourcePane")).IsEnabled ||
                !((FrameworkElement)window.FindName("OptimizerActivityDock")).IsVisible)
                throw new InvalidOperationException("Preparation pause did not preserve frozen inputs and a resumable dock.");
            ((Button)window.FindName("DockStopButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (Field(window, "_preparationPaused") is not false)
                throw new InvalidOperationException("Preparation stop did not release paused inputs.");
            Configure(window);
            Start(window, approve: false, pause: false);
            if (Field(window, "_deckBuildJob") is not null ||
                ((ProgressBar)window.FindName("OptimizationProgressBar")).IsIndeterminate)
                throw new InvalidOperationException("Cancelled proof preflight left a job or indeterminate bar.");
            Start(window, approve: true, pause: true);
            if (Field(window, "_deckBuildJob") is not DeckBuildJob { State: DeckBuildState.Paused })
                throw new InvalidOperationException("Proof click workflow did not pause.");
            window.Close();
            WaitUntil(() => !window.IsVisible, TimeSpan.FromSeconds(5), "Workflow checkpoint close");
            window = Open();
            Configure(window);
            Start(window, approve: true, pause: false);
            RequireProven(window);
            var directory = Path.Combine(DesktopSettingsStore.SettingsDirectory, "proof-checkpoints");
            var before = Directory.GetFiles(directory, "*.json").ToDictionary(path => path, File.ReadAllBytes);
            ((CheckBox)window.FindName("NewProofCheckpointCheckBox")).IsChecked = true;
            Start(window, approve: true, pause: false);
            RequireProven(window);
            if (!before.Any(pair => Directory.GetFiles(directory, Path.GetFileName(pair.Key) + ".previous-*")
                    .Any(backup => File.ReadAllBytes(backup).SequenceEqual(pair.Value))))
                throw new InvalidOperationException("Explicit restart did not preserve the previous proof bytes.");
        }
        finally
        {
            if (window is { IsVisible: true })
            {
                window.Close();
                WaitUntil(() => !window.IsVisible, TimeSpan.FromSeconds(5), "Workflow shutdown");
            }
            DesktopSettingsStore.Save(original);
        }
    }
    private static MainWindow Open()
    {
        var window = new MainWindow(false) { ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        WaitUntil(() => ((List<OwnedCardRow>)Field(window, "_ownedCardRows")!).Count == 722, TimeSpan.FromSeconds(5), "Proof workflow startup");
        return window;
    }
    private static void Configure(MainWindow window)
    {
        ((TabControl)window.FindName("WorkspaceTabs")).SelectedItem = (TabItem)window.FindName("OwnedOptimizerTab");
        ((ComboBox)window.FindName("CampaignScopeCombo")).SelectedIndex = 3;
        ((ComboBox)window.FindName("OptimizerProfileCombo")).SelectedValue = DeckStrategyProfile.Balanced;
        ((CheckBox)window.FindName("OptimizerIncludeGlitchesCheckBox")).IsChecked = false;
        ((CheckBox)window.FindName("ProveOptimalCheckBox")).IsChecked = true;
    }
    private static void Start(MainWindow window, bool approve, bool pause)
    {
        var handled = false;
        var paused = false;
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.Current.Windows.OfType<ProofConfirmationWindow>().FirstOrDefault(d => d.IsVisible);
            if (!handled && dialog is not null)
            {
                handled = true;
                dialog.DialogResult = approve;
            }
            if (pause && !paused && Field(window, "_deckBuildJob") is DeckBuildJob job &&
                job.State is DeckBuildState.Searching or DeckBuildState.Verifying or DeckBuildState.Preparing)
            {
                paused = true;
                ((Button)window.FindName("DockPauseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        };
        timer.Start();
        try
        {
            var build = (Button)window.FindName("OptimizeDeckButton");
            build.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitUntil(() => build.IsEnabled, TimeSpan.FromSeconds(30), "Complete proof button workflow");
            if (!handled) throw new InvalidOperationException("Proof workflow skipped confirmation.");
        }
        finally { timer.Stop(); }
    }
    private static void RequireProven(MainWindow window)
    {
        if (Field(window, "_deckBuildJob") is not DeckBuildJob { LastResult.ProvenOptimal: true } ||
            ((ProgressBar)window.FindName("OptimizationProgressBar")).Value != 1)
            throw new InvalidOperationException("Proof workflow did not install a completed proof.");
    }
}
