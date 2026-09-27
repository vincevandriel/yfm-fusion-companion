using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YfmCompanion.Data;
using YfmCompanion.Desktop;
using YfmCompanion.Desktop.Controls;
using YfmCompanion.Engine;
using YfmCompanion.RetroArch;
using static Program;

internal static class VisualStateAudit
{
    internal static int Run(MainWindow window, string fixtureSave, string output)
    {
        var catalog = (FusionCatalog)Field(window, "_catalog")!;
        var collection = (CollectionSnapshot)Field(window, "_collectionSnapshot")!;
        var tabs = (TabControl)window.FindName("WorkspaceTabs");
        var scroll = (ScrollViewer)window.FindName("FullWorkspaceScroll");
        var names = new[] { "turn", "deck", "live", "optimizer" };
        var details = new[] { "TurnResultsGrid", "DeckResultsGrid", "LiveAdviceGrid", "OwnedCardsGallery" };
        var middle = new[] { "MonsterPickerPanel", "AnyFusionMetric", "LivePlayerLpText", "OptimizerSpeedCombo" };
        var manifest = new List<object>();
        var pair = catalog.FusionPairs.First(pair => pair.IsIntended && !pair.IsGlitch && pair.MaterialLowId != pair.MaterialHighId);
        var hand = new[] { pair.MaterialLowId, pair.MaterialHighId, 1, 2, 3 };
        var deck = collection.Save.DeckCardIds.ToArray();
        Console.WriteLine("Visual state audit: preparing synthetic analyses.");
        var analysis = new DeckAnalyzer(catalog).Analyze(deck);
        var owned = collection.OwnedQuantities.Select((quantity, index) => new OwnedCardQuantity(index + 1, quantity)).Where(row => row.Quantity > 0).ToArray();
        SetField(window, "_resultOwned", owned.ToDictionary(row => row.CardId, row => row.Quantity));
        var job = new DeckBuildJob(catalog, new(owned, new(IncludeGlitches: false), DeckSearchMode.Quick));
        SetField(window, "_deckBuildJob", job);
        Await((Task)Invoke(window, "RunDeckBuildJobAsync", job, 0L, false)!);
        var result = job.LastResult!;
        var snapshot = new ForbiddenMemoriesLiveSnapshot(new(RetroArchPlaybackState.Playing, "PSX", "Synthetic Forbidden Memories", null, "synthetic"),
            true, hand, [new(1, 1, 3000, 2500), new(3, 22, 2500, 1200)], [new(2, 330, 0, 0)],
            [new(2, 22, 2500, 1200)], [], 0, deck, deck, collection.Save.ChestQuantities.Select(value => (int)value).ToArray(), true, 6400, 3700, 0, DateTimeOffset.UtcNow, "SYNTHETIC");
        var sizes = new[] { (1366, 768, 1.25), (1366, 768, 1.5), (1366, 768, 2d), (1920, 1080, 1.25), (1920, 1080, 1.5), (1920, 1080, 2d) };
        foreach (var state in new[] { "empty", "populated", "loading", "error", "completed" })
        {
            Populate();
            if (state == "empty")
            {
                Invoke(window, "ClearTurn_Click", window, new RoutedEventArgs());
                Invoke(window, "ClearDeck_Click", window, new RoutedEventArgs());
                Invoke(window, "ClearOwned_Click", window, new RoutedEventArgs());
                Invoke(window, "SetLiveUnavailable", "No live snapshot", "Start RetroArch to connect. Manual analysis is available.", "DISCONNECTED", "#7B3B45");
            }
            else if (state == "populated")
            {
                ((DataGrid)window.FindName("TurnResultsGrid")).ItemsSource = null;
                ((DataGrid)window.FindName("DeckResultsGrid")).ItemsSource = null;
                Invoke(window, "ResetOptimizerResults");
                Text("OptimizationStatus", "Collection loaded. Build a deck when ready.");
                Text("TurnResultSummary", "Cards entered. Analyze to see ordered routes.");
                Text("DeckResultSummary", "40 cards loaded. Analyze to evaluate opening hands.");
            }
            else if (state == "loading")
            {
                Invoke(window, "SetOptimizerRunning", true);
                Invoke(window, "ShowDeckBuildProgress", new DeckBuildProgress(DeckBuildState.Verifying, "Verifying finalist", TimeSpan.FromSeconds(64), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60), 2500, result.Best, null, 213472, 658008));
                Text("DeckResultSummary", "Examining 213,472 / 658,008 physical hands");
                ((ProgressBar)window.FindName("DeckAnalysisProgressBar")).Value = 213472d / 658008;
                ((Button)window.FindName("AnalyzeDeckButton")).IsEnabled = false;
                ((Button)window.FindName("CancelDeckButton")).IsEnabled = true;
                Text("LiveStatusText", "Waiting for the next validated read; previous snapshot retained.");
            }
            else if (state == "error")
            {
                Invoke(window, "InstallDeckBuildResult", result with { State = DeckBuildState.Failed });
                Text("OptimizationStatus", "Build failed: synthetic I/O failure. Best completed deck retained.");
                Text("DeckResultSummary", "Analysis failed: synthetic cancellation/error state. Retry is available.");
                Invoke(window, "ShowCollectionSource", collection with { IsStale = true, StaleReason = "Selected file is temporarily unavailable." });
                Invoke(window, "SetLiveUnavailable", "Read failed", "The next one-second refresh will retry. No game data was modified.", "ERROR", "#7B3B45");
            }
            for (var tab = 0; tab < names.Length; tab++)
            {
                tabs.SelectedIndex = tab;
                foreach (var (width, height, scale) in sizes)
                {
                    scroll.ScrollToTop();
                    var prefix = $"{state}-{names[tab]}-{width}x{height}-{scale.ToString("0.##", CultureInfo.InvariantCulture)}";
                    Capture(window, Path.Combine(output, prefix + "-top.png"), width / scale, height / scale, scale);
                    var clipped = ClippedText(window);
                    ((FrameworkElement)window.FindName(middle[tab])).BringIntoView();
                    Pump(TimeSpan.FromMilliseconds(30));
                    Capture(window, Path.Combine(output, prefix + "-middle.png"), width / scale, height / scale, scale);
                    clipped = clipped.Concat(ClippedText(window)).Distinct().ToArray();
                    ((FrameworkElement)window.FindName(details[tab])).BringIntoView();
                    Pump(TimeSpan.FromMilliseconds(30));
                    Capture(window, Path.Combine(output, prefix + "-detail.png"), width / scale, height / scale, scale);
                    manifest.Add(new
                    {
                        State = state,
                        Tab = names[tab],
                        Width = width,
                        Height = height,
                        Scale = scale,
                        Top = prefix + "-top.png",
                        Middle = prefix + "-middle.png",
                        Detail = prefix + "-detail.png",
                        ClippedText = clipped.Concat(ClippedText(window)).Distinct().ToArray()
                    });
                    if (state == "loading")
                    {
                        var dock = (FrameworkElement)window.FindName("OptimizerActivityDock");
                        var position = dock.TranslatePoint(new Point(), window);
                        if (!dock.IsVisible || position.Y < 0 || position.Y + dock.ActualHeight > window.ActualHeight)
                            throw new InvalidOperationException("Active-job dock left the window while scrolling.");
                    }
                }
            }
            Console.WriteLine($"Visual state audit captured {state} across all tabs/sizes.");
        }
        Populate();
        tabs.SelectedItem = (TabItem)window.FindName("OwnedOptimizerTab");
        ((FrameworkElement)window.FindName("CampaignScopeCombo")).BringIntoView();
        var combo = (ComboBox)window.FindName("CampaignScopeCombo");
        combo.IsDropDownOpen = true;
        Pump(TimeSpan.FromMilliseconds(200));
        var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
        var popupBody = (FrameworkElement)popup.Child;
        var popupBitmap = new RenderTargetBitmap((int)Math.Ceiling(popupBody.ActualWidth), (int)Math.Ceiling(popupBody.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        popupBitmap.Render(popupBody);
        var popupPng = new PngBitmapEncoder();
        popupPng.Frames.Add(BitmapFrame.Create(popupBitmap));
        using (var file = File.Create(Path.Combine(output, "strategy-dropdown.png"))) popupPng.Save(file);
        combo.IsDropDownOpen = false;
        tabs.SelectedIndex = 2;
        Invoke(window, "ApplyCompactMode", true, false);
        Invoke(window, "ShowLiveSnapshot", snapshot);
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
            Capture(window, Path.Combine(output, $"compact-{scale.ToString("0.##", CultureInfo.InvariantCulture)}.png"), 272, 1002, scale);
        if (window.MinWidth != 272 || window.MinHeight != 1002 || window.MaxWidth != 272 || window.MaxHeight != 1002 || ((FrameworkElement)window.FindName("FullWorkspace")).Visibility == Visibility.Visible)
            throw new InvalidOperationException("Compact mode did not preserve its bounded dimensions and hidden full content.");
        File.WriteAllText(Path.Combine(output, "visual-states.json"), JsonSerializer.Serialize(new
        {
            SyntheticOnly = true,
            LiveNetworkDisabled = true,
            Scope = "WPF logical-size/scaled render matrix; not OS DPI switching",
            HostDpiScale = VisualTreeHelper.GetDpi(window).DpiScaleX,
            Frames = manifest,
            ScreenshotCount = Directory.GetFiles(output, "*.png").Length
        }, new JsonSerializerOptions { WriteIndented = true }));
        window.Close();
        WaitUntil(() => !window.IsVisible, TimeSpan.FromSeconds(5), "Visual audit shutdown");
        Console.WriteLine($"Visual state matrix complete: {manifest.Count} cases plus Compact Live.");
        return 0;

        void Text(string name, string text) => ((TextBlock)window.FindName(name)).Text = text;
        void Populate()
        {
            SetField(window, "_deckBuildJob", job);
            Invoke(window, "SetOptimizerRunning", false);
            Invoke(window, "ChangeCollectionSource", CollectionSourceMode.PinnedFile);
            SetField(window, "_pinnedSavePath", fixtureSave);
            Invoke(window, "ApplyCollectionSnapshot", collection, true);
            Invoke(window, "LoadDeckAnalyzerFromSnapshot", collection.Save);
            var pickers = (List<CardPicker>)Field(window, "_handPickers")!;
            for (var index = 0; index < hand.Length; index++) pickers[index].SetCard(catalog.GetCard(hand[index]));
            Invoke(window, "AnalyzeTurn_Click", window, new RoutedEventArgs());
            Invoke(window, "ShowDeckReport", analysis);
            ((DataGrid)window.FindName("TurnResultsGrid")).SelectedIndex = 0;
            ((DataGrid)window.FindName("DeckResultsGrid")).SelectedIndex = 0;
            ((Button)window.FindName("AnalyzeDeckButton")).IsEnabled = true;
            ((Button)window.FindName("CancelDeckButton")).IsEnabled = false;
            Invoke(window, "ShowLiveSnapshot", snapshot);
            Invoke(window, "InstallDeckBuildResult", result);
        }
    }

    private static string[] ClippedText(Window window)
    {
        var issues = new List<string>();
        foreach (var text in Descendants(window).OfType<TextBlock>())
        {
            if (!text.IsVisible || text.ActualWidth <= 1 || string.IsNullOrWhiteSpace(text.Text) || text.TextWrapping != TextWrapping.NoWrap || text.TextTrimming != TextTrimming.None) continue;
            var position = text.TranslatePoint(new Point(), window);
            if (position.Y < 0 || position.Y + text.ActualHeight > window.ActualHeight || position.X < 0 || position.X > window.ActualWidth) continue;
            if (Ancestors(text).Any(parent => parent is DataGridCell or DataGridColumnHeader)) continue;
            var measured = new FormattedText(text.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.White, VisualTreeHelper.GetDpi(text).PixelsPerDip);
            if (measured.Width > text.ActualWidth + 3) issues.Add($"{text.Name}: {text.Text}");
        }
        return issues.Distinct().ToArray();
    }
    private static IEnumerable<DependencyObject> Ancestors(DependencyObject node)
    {
        while ((node = VisualTreeHelper.GetParent(node)) is not null) yield return node;
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
