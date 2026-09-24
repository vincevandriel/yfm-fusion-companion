using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YfmCompanion.Desktop;
using YfmCompanion.RetroArch;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("phase2-ui");
        Directory.CreateDirectory(output);
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YFM Fusion Companion", "settings.json");
        var settingsBackup = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        var fixtureDirectory = Path.Combine(Path.GetTempPath(), "yfm-phase2-ui-audit");
        Directory.CreateDirectory(fixtureDirectory);
        var fixtureSave = Path.Combine(fixtureDirectory, "Synthetic-Forbidden-Memories.srm");
        File.WriteAllBytes(fixtureSave, CreateSyntheticSave());
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(new
        {
            Left = 0, Top = 0, Width = 1280, Height = 720, IsMaximized = false, AlwaysOnTop = false,
            CompactMode = false, LastSavePath = fixtureSave, CollectionSourceMode = 1,
            KnownSaveLocations = new[] { fixtureDirectory }, ArtworkFolder = (string?)null
        }));
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var window = new MainWindow
        {
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop
        };
        window.Show();
        Pump(TimeSpan.FromSeconds(3));
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

        ((ComboBox)window.FindName("OptimizerSpeedCombo")).SelectedIndex = 2;
        optimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var pauseAvailableClock = Stopwatch.StartNew();
        while (!pauseButton.IsEnabled && pauseAvailableClock.Elapsed < TimeSpan.FromSeconds(5)) Pump(TimeSpan.FromMilliseconds(20));
        if (!pauseButton.IsEnabled) throw new InvalidOperationException("Pause did not become available within five seconds.");
        var pauseClock = Stopwatch.StartNew();
        pauseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        while (!optimizeButton.IsEnabled && pauseClock.Elapsed < TimeSpan.FromSeconds(3)) Pump(TimeSpan.FromMilliseconds(20));
        var pauseMilliseconds = pauseClock.Elapsed.TotalMilliseconds;
        optimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var stopAvailableClock = Stopwatch.StartNew();
        while (!stopButton.IsEnabled && stopAvailableClock.Elapsed < TimeSpan.FromSeconds(5)) Pump(TimeSpan.FromMilliseconds(20));
        if (!stopButton.IsEnabled) throw new InvalidOperationException("Stop did not become available within five seconds after resume.");
        var stopClock = Stopwatch.StartNew();
        stopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        while (!optimizeButton.IsEnabled && stopClock.Elapsed < TimeSpan.FromSeconds(3)) Pump(TimeSpan.FromMilliseconds(20));
        var stopMilliseconds = stopClock.Elapsed.TotalMilliseconds;

        var focusVisits = AuditKeyboardNavigation(window);
        File.WriteAllText(Path.Combine(output, "keyboard-navigation.txt"), $"Forward focus visits: {focusVisits}{Environment.NewLine}");
        File.WriteAllText(Path.Combine(output, "ui-audit.json"), JsonSerializer.Serialize(new
        {
            SyntheticSave = true,
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
        app.Shutdown();
        if (settingsBackup is null)
        {
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllBytes(settingsPath, settingsBackup);
        }
        Directory.Delete(fixtureDirectory, recursive: true);
        Console.WriteLine($"Captured {Directory.GetFiles(output, "*.png").Length} UI audit images; busy {busyMilliseconds:N0} ms; first deck {firstCandidateMilliseconds:N0} ms; pause {pauseMilliseconds:N0} ms; stop {stopMilliseconds:N0} ms; keyboard focus visits {focusVisits}; output {output}");
        return 0;
    }

    private static void Capture(Window window, string path, double widthDip, double heightDip, double scale)
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

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static int AuditKeyboardNavigation(Window window)
    {
        ((Button)window.FindName("OptimizeDeckButton")).Focus();
        var visited = new HashSet<IInputElement>();
        for (var index = 0; index < 40; index++)
        {
            if (Keyboard.FocusedElement is { } focused) visited.Add(focused);
            if (!(Keyboard.FocusedElement as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) ?? true) break;
        }
        if (visited.Count < 8) throw new InvalidOperationException($"Keyboard navigation reached only {visited.Count} distinct controls.");
        return visited.Count;
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
