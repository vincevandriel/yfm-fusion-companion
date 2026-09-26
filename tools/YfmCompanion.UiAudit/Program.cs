using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YfmCompanion.Desktop;
using YfmCompanion.Desktop.Controls;
using YfmCompanion.Engine;
using YfmCompanion.RetroArch;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var fixtureDirectory = Directory.CreateTempSubdirectory("yfm-ui-audit-").FullName;
        var previousSettingsDirectory = Environment.GetEnvironmentVariable("YFM_COMPANION_SETTINGS_DIRECTORY");
        Environment.SetEnvironmentVariable("YFM_COMPANION_SETTINGS_DIRECTORY", fixtureDirectory);
        try { return Run(args, fixtureDirectory); }
        finally
        {
            Environment.SetEnvironmentVariable("YFM_COMPANION_SETTINGS_DIRECTORY", previousSettingsDirectory);
            Application.Current?.Shutdown();
            Directory.Delete(fixtureDirectory, recursive: true);
        }
    }

    private static int Run(string[] args, string fixtureDirectory)
    {
        var realLive = args.Contains("--real-live", StringComparer.Ordinal);
        if (!realLive) DesktopContractAudit.Run(fixtureDirectory);
        var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("phase2-ui");
        Directory.CreateDirectory(output);
        var settingsPath = Path.Combine(fixtureDirectory, "settings.json");
        var fixtureSave = Path.Combine(fixtureDirectory, "Synthetic-Forbidden-Memories.srm");
        File.WriteAllBytes(fixtureSave, CreateSyntheticSave());
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(new
        {
            Left = 0,
            Top = 0,
            Width = 1280,
            Height = 720,
            IsMaximized = false,
            AlwaysOnTop = false,
            CompactMode = false,
            LastSavePath = fixtureSave,
            CollectionSourceMode = 1,
            KnownSaveLocations = new[] { fixtureDirectory },
            ArtworkFolder = (string?)null
        }));
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var window = new MainWindow(backgroundServicesEnabled: false)
        {
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop
        };
        window.Show();
        Pump(TimeSpan.FromSeconds(3));
        if (realLive) return RealLivePresentationAudit.Run(window, output);
        if (args.Contains("--live-interaction", StringComparer.Ordinal))
        {
            DesktopInteractionAudit.Run(window);
            File.WriteAllText(Path.Combine(output, "live-interaction.json"), JsonSerializer.Serialize(new
            {
                Passed = true,
                Synthetic = true,
                Checks = "Changing snapshots, transient read/advice withholding, recovery, deck selection/scroll retention, disconnect clearing, fixed optimizer progress and read-only gallery"
            }, new JsonSerializerOptions { WriteIndented = true }));
            window.Close();
            return 0;
        }
        if (args.Contains("--states", StringComparer.Ordinal)) return VisualStateAudit.Run(window, fixtureSave, output);
        var fullWorkspace = (FrameworkElement)window.FindName("FullWorkspace");
        if (fullWorkspace.Visibility != Visibility.Visible)
        {
            ((Button)window.FindName("CompactModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(TimeSpan.FromMilliseconds(150));
        }
        var tabs = (TabControl)window.FindName("WorkspaceTabs");

        var normalTabs = new[] { "turn-adviser", "deck-analyzer", "live-duel", "save-snapshot", "owned-optimizer" };
        for (var index = 0; index < normalTabs.Length; index++)
        {
            tabs.SelectedIndex = index;
            Pump(TimeSpan.FromMilliseconds(150));
            Capture(window, Path.Combine(output, $"1920x1080-150-{normalTabs[index]}.png"), 1280, 720, 1.5);
        }

        tabs.SelectedIndex = 4;
        foreach (var scenario in new[]
        {
            (1366, 768, 1.25), (1366, 768, 1.5), (1366, 768, 2.0),
            (1920, 1080, 1.25), (1920, 1080, 1.5), (1920, 1080, 2.0)
        })
        {
            Capture(window, Path.Combine(output, $"{scenario.Item1}x{scenario.Item2}-{scenario.Item3:0.##}-owned-optimizer.png"),
                scenario.Item1 / scenario.Item3, scenario.Item2 / scenario.Item3, scenario.Item3);
        }

        var workspaceScroll = (ScrollViewer)window.FindName("FullWorkspaceScroll");
        window.Width = 1280;
        window.Height = 720;
        workspaceScroll.ScrollToVerticalOffset(520);
        Pump(TimeSpan.FromMilliseconds(150));
        Capture(window, Path.Combine(output, "1920x1080-150-owned-gallery.png"), 1280, 720, 1.5);
        workspaceScroll.ScrollToTop();
        ((ComboBox)window.FindName("OptimizerSpeedCombo")).SelectedIndex = 0;
        var optimizeButton = (Button)window.FindName("OptimizeDeckButton");
        var pauseButton = (Button)window.FindName("PauseOptimizationButton");
        var stopButton = (Button)window.FindName("StopOptimizationButton");
        var optimizedDeck = (DataGrid)window.FindName("OptimizedDeckGrid");
        var startClock = Stopwatch.StartNew();
        optimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        while (!pauseButton.IsEnabled && startClock.Elapsed < TimeSpan.FromSeconds(3)) Pump(TimeSpan.FromMilliseconds(20));
        var busyMilliseconds = startClock.Elapsed.TotalMilliseconds;
        Pump(TimeSpan.FromMilliseconds(600));
        Capture(window, Path.Combine(output, "1920x1080-150-quick-progress.png"), 1280, 720, 1.5);
        while (optimizedDeck.Items.Count == 0 && startClock.Elapsed < TimeSpan.FromSeconds(8)) Pump(TimeSpan.FromMilliseconds(40));
        var firstCandidateMilliseconds = startClock.Elapsed.TotalMilliseconds;
        while (!optimizeButton.IsEnabled && startClock.Elapsed < TimeSpan.FromSeconds(15)) Pump(TimeSpan.FromMilliseconds(50));
        Capture(window, Path.Combine(output, "1920x1080-150-quick-result.png"), 1280, 720, 1.5);
        workspaceScroll.ScrollToVerticalOffset(520);
        Pump(TimeSpan.FromMilliseconds(150));
        Capture(window, Path.Combine(output, "1920x1080-150-quick-result-deck.png"), 1280, 720, 1.5);
        workspaceScroll.ScrollToTop();

        AuditVerification(window);

        ((ComboBox)window.FindName("OptimizerSpeedCombo")).SelectedIndex = 2;
        optimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var pauseAvailableClock = Stopwatch.StartNew();
        while (CurrentJob(window)?.State is not (DeckBuildState.Searching or DeckBuildState.Verifying) && pauseAvailableClock.Elapsed < TimeSpan.FromSeconds(5)) Pump(TimeSpan.FromMilliseconds(20));
        if (CurrentJob(window)?.State is not (DeckBuildState.Searching or DeckBuildState.Verifying))
            throw new InvalidOperationException("A real search did not start within five seconds.");
        var pauseClock = Stopwatch.StartNew();
        pauseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        while (!optimizeButton.IsEnabled && pauseClock.Elapsed < TimeSpan.FromSeconds(3)) Pump(TimeSpan.FromMilliseconds(20));
        var pauseMilliseconds = pauseClock.Elapsed.TotalMilliseconds;
        if (CurrentJob(window)?.State != DeckBuildState.Paused) throw new InvalidOperationException("Pause did not preserve a paused job.");
        AuditSaveDuringPause(window, fixtureSave);
        optimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var stopAvailableClock = Stopwatch.StartNew();
        while (!stopButton.IsEnabled && stopAvailableClock.Elapsed < TimeSpan.FromSeconds(5)) Pump(TimeSpan.FromMilliseconds(20));
        if (!stopButton.IsEnabled) throw new InvalidOperationException("Stop did not become available within five seconds after resume.");
        var stopClock = Stopwatch.StartNew();
        stopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        while (!optimizeButton.IsEnabled && stopClock.Elapsed < TimeSpan.FromSeconds(3)) Pump(TimeSpan.FromMilliseconds(20));
        var stopMilliseconds = stopClock.Elapsed.TotalMilliseconds;
        if (CurrentJob(window)?.State != DeckBuildState.Cancelled) throw new InvalidOperationException("Stop did not cancel the resumed job.");
        if (Field(window, "_pendingCollectionSnapshot") is not null)
            throw new InvalidOperationException("The pending save was not applied after stop.");

        if (busyMilliseconds > 200 || firstCandidateMilliseconds > 5000 ||
            pauseMilliseconds > 1000 || stopMilliseconds > 1000)
            throw new InvalidOperationException($"Responsiveness gate failed: busy {busyMilliseconds}, first {firstCandidateMilliseconds}, pause {pauseMilliseconds}, stop {stopMilliseconds} ms.");

        AuditSourceTransitions(window, fixtureSave);
        AuditManualPersistence(window);
        AuditCardEntryAndAnalysis(window);
        AuditGallery(window);
        DesktopInteractionAudit.Run(window);
        AuditProofRecovery(window, fixtureDirectory);
        DesktopLifecycleAudit.Run(window, fixtureDirectory, fixtureSave);
        var focusVisits = AuditKeyboardNavigation(window);
        File.WriteAllText(Path.Combine(output, "keyboard-navigation.txt"), $"Forward focus visits: {focusVisits}{Environment.NewLine}");
        File.WriteAllText(Path.Combine(output, "ui-audit.json"), JsonSerializer.Serialize(new
        {
            SyntheticSave = true,
            IsolatedSettings = true,
            ProofWorkflowChecks = "real Build button; preflight; cancel; approve; pause; close; reopen; resume; proven installation; explicit restart preserves original checkpoint bytes; preparation pause freezes inputs",
            PresentationChecks = "docked progress stays visible; collection browsable but quantity read-only during a job; 20 changed synthetic live snapshots retain deck selection and scroll; disconnect clears stale rows",
            SourceTransitionChecks = "missing -> stale -> recovered; in-flight refresh -> protected manual edit",
            VerificationChecks = "pause -> resume -> exact Ready; late callbacks do not overwrite terminal state",
            DesktopContracts = "progress labels; thumbnail LRU, replacement pixels, corruption, deletion, oversize; manual persistence; pending save during pause; stable live selection; adaptive layout; autocomplete clearing; analysis cancellation; deck tray add/remove/copy counts; gallery virtualization; proof desktop restart and incompatibility; pinned missing/recovery/race; real folder watcher; proof confirmation; close during proof and durable resume",
            BusyStateMilliseconds = busyMilliseconds,
            FirstCandidateMilliseconds = firstCandidateMilliseconds,
            PauseResponseMilliseconds = pauseMilliseconds,
            StopResponseMilliseconds = stopMilliseconds,
            KeyboardFocusVisits = focusVisits,
            ScreenshotCount = Directory.GetFiles(output, "*.png").Length,
            OptimizerStatus = ((TextBlock)window.FindName("OptimizationStatus")).Text,
            ProgressDetail = ((TextBlock)window.FindName("OptimizationProgressDetail")).Text
        }, new JsonSerializerOptions { WriteIndented = true }));

        window.Close();
        WaitUntil(() => !window.IsVisible, TimeSpan.FromSeconds(5), "Main audit shutdown");
        app.Shutdown();
        Console.WriteLine($"Captured {Directory.GetFiles(output, "*.png").Length} UI audit images; busy {busyMilliseconds:N0} ms; first deck {firstCandidateMilliseconds:N0} ms; pause {pauseMilliseconds:N0} ms; stop {stopMilliseconds:N0} ms; keyboard focus visits {focusVisits}; output {output}");
        return 0;
    }

    internal static void Capture(Window window, string path, double widthDip, double heightDip, double scale)
    {
        window.Width = Math.Max(window.MinWidth, widthDip);
        window.Height = Math.Max(window.MinHeight, heightDip);
        window.UpdateLayout();
        Pump(TimeSpan.FromMilliseconds(100));
        var pixelWidth = Math.Max(1, (int)Math.Round(window.ActualWidth * scale));
        var pixelHeight = Math.Max(1, (int)Math.Round(window.ActualHeight * scale));
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    internal static object? Invoke(MainWindow window, string method, params object?[] arguments)
    {
        var target = typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!;
        var parameters = target.GetParameters();
        var supplied = arguments.Concat(parameters.Skip(arguments.Length).Select(parameter => parameter.HasDefaultValue
            ? parameter.DefaultValue : throw new ArgumentException($"Missing audit argument: {method}.{parameter.Name}"))).ToArray();
        return target.Invoke(window, supplied);
    }

    private static DeckBuildJob? CurrentJob(MainWindow window) =>
        (DeckBuildJob?)typeof(MainWindow).GetField("_deckBuildJob", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);

    internal static object? Field(MainWindow window, string name) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);

    internal static void SetField(MainWindow window, string name, object value) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);

    private static void AuditProofRecovery(MainWindow window, string directory)
    {
        var catalog = (YfmCompanion.Data.FusionCatalog)Field(window, "_catalog")!;
        var owned = Enumerable.Range(1, 14).Select(id => new OwnedCardQuantity(id, id == 14 ? 1 : 3)).ToArray();
        var request = new DeckBuildRequest(owned, new(IncludeGlitches: false), DeckSearchMode.ProveOptimal, SourceIdentity: "synthetic-proof");
        var checkpoint = Path.Combine(directory, "desktop-proof.json");
        SetField(window, "_resultOwned", owned.ToDictionary(row => row.CardId, row => row.Quantity));
        SetField(window, "_resultStarChips", 0U);
        Task Start(DeckBuildJob job)
        {
            SetField(window, "_deckBuildJob", job);
            var generation = (long)Field(window, "_deckBuildGeneration")! + 1;
            SetField(window, "_deckBuildGeneration", generation);
            return (Task)Invoke(window, "RunDeckBuildJobAsync", job, generation, false)!;
        }
        var first = new DeckBuildJob(catalog, request, checkpoint);
        var pausing = Start(first);
        first.Pause();
        Await(pausing);
        if (first.State != DeckBuildState.Paused || !File.Exists(checkpoint) ||
            !((TextBlock)window.FindName("OptimizationProgressDetail")).Text.Contains("checkpointed to disk", StringComparison.Ordinal))
            throw new InvalidOperationException("Desktop proof pause did not preserve its durable checkpoint.");
        var recovered = new DeckBuildJob(catalog, request, checkpoint);
        Await(Start(recovered));
        if (recovered.LastResult is not { ProvenOptimal: true, Best.Report.ExactAnalysis.TotalHands: 658008 } ||
            !((TextBlock)window.FindName("OptimizationStatus")).Text.StartsWith("Proven optimal", StringComparison.Ordinal))
            throw new InvalidOperationException("Desktop proof restart did not install the completed one-deck proof.");
        var incompatible = new DeckBuildJob(catalog, request with { SourceIdentity = "different-input" }, checkpoint);
        try { Await(Start(incompatible)); throw new InvalidOperationException("Incompatible proof checkpoint was accepted."); }
        catch (InvalidDataException error) when (error.Message.Contains("Cannot resume", StringComparison.Ordinal)) { }
        if (!((TextBlock)window.FindName("OptimizationStageText")).Text.StartsWith("FAILED", StringComparison.Ordinal) ||
            ((TextBlock)window.FindName("OptimizationStatus")).Text.StartsWith("Proven optimal", StringComparison.Ordinal) ||
            ((ProgressBar)window.FindName("OptimizationProgressBar")).IsIndeterminate ||
            ((Button)window.FindName("VerifyOptimizationButton")).IsEnabled)
            throw new InvalidOperationException("Proof failure left misleading progress or enabled verification.");
    }

    private static void AuditSaveDuringPause(MainWindow window, string save)
    {
        var frozen = (CollectionSnapshot)Field(window, "_collectionSnapshot")!;
        var bytes = File.ReadAllBytes(save);
        foreach (var offset in new[] { Ps1MemoryCardReader.FirstSaveCopyOffset, Ps1MemoryCardReader.SecondSaveCopyOffset })
            bytes[Ps1MemoryCardReader.BlockSize + offset + Ps1MemoryCardReader.ChestOffset + 40] = 0;
        File.WriteAllBytes(save, bytes);
        Await((Task)Invoke(window, "RefreshCollectionAsync", false)!);
        if (Field(window, "_pendingCollectionSnapshot") is not CollectionSnapshot pending || pending.ContentIdentity == frozen.ContentIdentity ||
            !ReferenceEquals(frozen, Field(window, "_collectionSnapshot")))
            throw new InvalidOperationException("A changed save was not frozen/pending during pause.");
    }

    private static void AuditManualPersistence(MainWindow window)
    {
        var rows = (List<OwnedCardRow>)Field(window, "_ownedCardRows")!;
        rows[0].Quantity = 120;
        rows[1].Quantity = 0;
        rows[0].ProposedCopies = 2;
        rows[0].Quantity++;
        var settings = DesktopSettingsStore.Load();
        if (settings.CollectionSourceMode != CollectionSourceMode.Manual || settings.ManualQuantities?.GetValueOrDefault(1) != 121 ||
            settings.ManualQuantities.ContainsKey(2) || !rows[0].QuantityLine.Contains("manual", StringComparison.Ordinal) ||
            rows.Any(row => row.ProposedCopies != 0) || ((Button)window.FindName("VerifyOptimizationButton")).IsEnabled)
            throw new InvalidOperationException("Manual quantities/provenance/result reset were not preserved correctly.");
        Invoke(window, "SaveDesktopSettings");
        var restored = new MainWindow(backgroundServicesEnabled: false) { ShowActivated = false, ShowInTaskbar = false };
        restored.Show();
        WaitUntil(() => ((List<OwnedCardRow>)Field(restored, "_ownedCardRows")!).Count > 0, TimeSpan.FromSeconds(3), "Manual restart");
        var restoredRows = (List<OwnedCardRow>)Field(restored, "_ownedCardRows")!;
        if (restoredRows[0].Quantity != 121 || restoredRows[1].Quantity != 0 ||
            ((TextBlock)restored.FindName("OptimizerSourceTitle")).Text != "MANUAL COLLECTION")
            throw new InvalidOperationException("Restart lost the manual collection.");
        restored.Close();
        WaitUntil(() => !restored.IsVisible, TimeSpan.FromSeconds(3), "Manual window shutdown");
    }

    internal static void Await(Task task)
    {
        var clock = Stopwatch.StartNew();
        while (!task.IsCompleted && clock.Elapsed < TimeSpan.FromSeconds(8)) Pump(TimeSpan.FromMilliseconds(20));
        if (!task.IsCompleted) throw new TimeoutException("Audit operation did not complete.");
        task.GetAwaiter().GetResult();
    }

    private static void AuditSourceTransitions(MainWindow window, string save)
    {
        var original = File.ReadAllBytes(save);
        File.Delete(save);
        Await((Task)Invoke(window, "RefreshCollectionAsync", false)!);
        var title = (TextBlock)window.FindName("OptimizerSourceTitle");
        if (!title.Text.Contains("STALE", StringComparison.Ordinal)) throw new InvalidOperationException("Missing source was not marked stale.");
        File.WriteAllBytes(save, original);
        Await((Task)Invoke(window, "RefreshCollectionAsync", false)!);
        if (title.Text.Contains("STALE", StringComparison.Ordinal)) throw new InvalidOperationException("Recovered source remained stale.");
        var pending = (Task)Invoke(window, "RefreshCollectionAsync", false)!;
        Invoke(window, "UseManualCollection_Click", window, new RoutedEventArgs());
        Await(pending);
        if (title.Text != "MANUAL COLLECTION") throw new InvalidOperationException("An in-flight save refresh overwrote manual mode.");
        var snapshot = typeof(MainWindow).GetField("_collectionSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
        if (snapshot is not null) throw new InvalidOperationException("Manual mode retained an automatic collection identity.");
    }

    private static void AuditVerification(MainWindow window)
    {
        var verify = (Button)window.FindName("VerifyOptimizationButton");
        var build = (Button)window.FindName("OptimizeDeckButton");
        var pause = (Button)window.FindName("PauseOptimizationButton");
        var bar = (ProgressBar)window.FindName("OptimizationProgressBar");
        var stage = (TextBlock)window.FindName("OptimizationStageText");
        if (!verify.IsEnabled) throw new InvalidOperationException("Quick result did not offer Verify deck.");
        verify.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        pause.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitUntil(() => build.IsEnabled, TimeSpan.FromSeconds(2), "Verification pause");
        if (CurrentJob(window)?.State != DeckBuildState.Paused || stage.Text != "PAUSED" || bar.IsIndeterminate || verify.IsEnabled)
            throw new InvalidOperationException("Verification pause has inconsistent state or controls.");
        build.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitUntil(() => build.IsEnabled, TimeSpan.FromSeconds(60), "Verification resume");
        Pump(TimeSpan.FromMilliseconds(350));
        var result = CurrentJob(window)?.LastResult;
        if (result?.State != DeckBuildState.Completed || result.Best?.Report.ExactAnalysis.IsExact != true ||
            result.Best.Report.ExactAnalysis.TotalHands != 658008 || result.ProvenOptimal ||
            !stage.Text.EndsWith("VERIFY ✓  READY", StringComparison.Ordinal) || bar.Value != 1 || bar.IsIndeterminate || verify.IsEnabled)
            throw new InvalidOperationException("Exact verification was not installed as Ready, or delayed progress overwrote it.");
    }

    internal static void WaitUntil(Func<bool> condition, TimeSpan timeout, string operation)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < timeout) Pump(TimeSpan.FromMilliseconds(20));
        if (!condition()) throw new TimeoutException($"{operation} did not finish within {timeout}.");
    }

    internal static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static int AuditKeyboardNavigation(Window window)
    {
        var tabs = (TabControl)window.FindName("WorkspaceTabs");
        var total = 0;
        for (var tab = 0; tab < tabs.Items.Count; tab++)
        {
            tabs.SelectedIndex = tab;
            Pump(TimeSpan.FromMilliseconds(40));
            var content = (UIElement)((TabItem)tabs.Items[tab]).Content;
            content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            foreach (var direction in new[] { FocusNavigationDirection.Next, FocusNavigationDirection.Previous })
            {
                var visited = new HashSet<IInputElement>();
                for (var index = 0; index < 60; index++)
                {
                    if (Keyboard.FocusedElement is { } focused) visited.Add(focused);
                    if (!(Keyboard.FocusedElement as UIElement)?.MoveFocus(new TraversalRequest(direction)) ?? true) break;
                }
                if (visited.Count < 4) throw new InvalidOperationException($"Tab {tab} {direction} navigation reached only {visited.Count} controls.");
                total += visited.Count;
            }
        }
        return total;
    }

    private static void AuditCardEntryAndAnalysis(MainWindow window)
    {
        var pickers = (List<CardPicker>)Field(window, "_deckPickers")!;
        var catalog = (YfmCompanion.Data.FusionCatalog)Field(window, "_catalog")!;
        var picker = pickers[0];
        ((TabControl)window.FindName("WorkspaceTabs")).SelectedIndex = 1;
        var input = (TextBox)picker.FindName("InputBox");
        input.Text = "Dragon";
        var list = (ListBox)picker.FindName("SuggestionList");
        if (list.Items.Count < 2) throw new InvalidOperationException("Autocomplete did not return multiple matches.");
        var selected = (YfmCompanion.Data.Card)list.Items[1];
        picker.SetCard(selected);
        if (list.Items.Count != 0 || picker.SelectedCard != selected)
            throw new InvalidOperationException("Selected autocomplete card retained stale Tab suggestions.");
        picker.Clear();
        if (picker.SelectedCard is not null || !((TextBlock)picker.FindName("CardDetails")).Text.Contains("Empty slot", StringComparison.Ordinal))
            throw new InvalidOperationException("Clearing a card retained stale details.");
        Invoke(window, "ClearDeck_Click", window, new RoutedEventArgs());
        var add = (CardPicker)Field(window, "_deckAddPicker")!;
        for (var copy = 0; copy < 3; copy++)
        {
            add.SetCard(catalog.GetCard(1));
            Invoke(window, "AddDeckCard_Click", window, new RoutedEventArgs());
        }
        var tray = (DeckTrayViewModel)Field(window, "_deckTray")!;
        if (tray.Cards.Count != 1 || tray.Cards[0].Copies != 3)
            throw new InvalidOperationException("Deck tray failed to group three physical copies.");
        var view = (YfmCompanion.Desktop.Views.DeckAnalyzerView)window.FindName("DeckAnalyzerPaneView");
        ((ListBox)view.FindName("DeckTrayGallery")).SelectedIndex = 0;
        Invoke(window, "RemoveDeckCard_Click", window, new RoutedEventArgs());
        if (tray.Cards.Count != 1 || tray.Cards[0].Copies != 2)
            throw new InvalidOperationException("Deck tray remove did not remove exactly one physical copy.");
        for (var index = 0; index < pickers.Count; index++) pickers[index].SetCard(catalog.GetCard(index + 1));
        add.SetCard(catalog.GetCard(1));
        Invoke(window, "AddDeckCard_Click", window, new RoutedEventArgs());
        if (pickers.Count(p => p.SelectedCard is not null) != 40 || tray.Cards.Sum(row => row.Copies) != 40)
            throw new InvalidOperationException("Deck tray exceeded 40 slots.");
        var analyze = (Button)window.FindName("AnalyzeDeckButton");
        analyze.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Invoke(window, "ClearDeck_Click", window, new RoutedEventArgs());
        WaitUntil(() => analyze.IsEnabled, TimeSpan.FromSeconds(3), "Changed-deck cancellation");
        Pump(TimeSpan.FromMilliseconds(100));
        if (((DataGrid)window.FindName("DeckResultsGrid")).Items.Count != 0 || ((ProgressBar)window.FindName("DeckAnalysisProgressBar")).Value != 0)
            throw new InvalidOperationException("An old analysis overwrote the cleared deck.");
    }

    private static void AuditGallery(MainWindow window)
    {
        ((TabControl)window.FindName("WorkspaceTabs")).SelectedIndex = 4;
        var gallery = (ListBox)window.FindName("OwnedCardsGallery");
        var source = gallery.ItemsSource;
        gallery.SelectedIndex = 3;
        var selected = gallery.SelectedItem;
        Invoke(window, "RefreshOwnedGallery");
        if (!ReferenceEquals(source, gallery.ItemsSource) || !ReferenceEquals(selected, gallery.SelectedItem))
            throw new InvalidOperationException("Gallery refresh reset stable items or selection.");
        gallery.UpdateLayout();
        var realized = Enumerable.Range(0, gallery.Items.Count).Count(index => gallery.ItemContainerGenerator.ContainerFromIndex(index) is not null);
        if (realized == 0 || realized >= gallery.Items.Count || realized > 30)
            throw new InvalidOperationException($"Gallery is not virtualized: {realized} / {gallery.Items.Count} containers.");
    }

    private static byte[] CreateSyntheticSave()
    {
        var bytes = Enumerable.Repeat((byte)0xFF, Ps1MemoryCardReader.MemoryCardBankSize).ToArray();
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'C';
        const int block = 1;
        var directoryOffset = block * 0x80;
        Array.Clear(bytes, directoryOffset, 0x80);
        bytes[directoryOffset] = 0x51;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(directoryOffset + 4, 4), Ps1MemoryCardReader.BlockSize);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(directoryOffset + 8, 2), ushort.MaxValue);
        Encoding.ASCII.GetBytes(Ps1MemoryCardReader.ForbiddenMemoriesSaveName).CopyTo(bytes, directoryOffset + 10);
        byte checksum = 0;
        for (var index = 0; index < 0x7F; index++) checksum ^= bytes[directoryOffset + index];
        bytes[directoryOffset + 0x7F] = checksum;
        var blockOffset = block * Ps1MemoryCardReader.BlockSize;
        bytes[blockOffset] = (byte)'S';
        bytes[blockOffset + 1] = (byte)'C';
        var save = new byte[Ps1MemoryCardReader.SaveCopyLength];
        for (var slot = 0; slot < Ps1MemoryCardReader.DeckSize; slot++)
            BinaryPrimitives.WriteUInt16LittleEndian(save.AsSpan(slot * 2, 2), (ushort)(slot + 1));
        for (var card = 41; card <= 100; card++) save[Ps1MemoryCardReader.ChestOffset + card - 1] = (byte)(card % 3 + 1);
        BinaryPrimitives.WriteUInt32LittleEndian(save.AsSpan(Ps1MemoryCardReader.StarChipsOffset, 4), 12_345);
        save.CopyTo(bytes, blockOffset + Ps1MemoryCardReader.FirstSaveCopyOffset);
        save.CopyTo(bytes, blockOffset + Ps1MemoryCardReader.SecondSaveCopyOffset);
        return bytes;
    }
}
