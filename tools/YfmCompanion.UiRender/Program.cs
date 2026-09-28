using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YfmCompanion.Data;
using YfmCompanion.Desktop;
using YfmCompanion.Engine;

namespace YfmCompanion.UiRender;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--startup-only"))
        {
            Console.Error.WriteLine("Usage: YfmCompanion.UiRender <output-directory> [--startup-only]");
            return 2;
        }

        var outputDirectory = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(outputDirectory);
        var application = new App();
        application.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

        var window = new MainWindow { Left = 0, Top = 0 };
        var initializeMethod = typeof(MainWindow).GetMethod("InitializeOfflineWorkspace", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Offline workspace initializer was not found.");
        initializeMethod.Invoke(window, null);
        if (args.Length == 2)
        {
            var status = window.FindName("DatabaseStatus") as TextBlock
                ?? throw new InvalidOperationException("Database status control was not found.");
            if (!status.Text.StartsWith("Offline database ready", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Offline database did not report ready status: {status.Text}");
            }

            window.Close();
            return 0;
        }
        var compactMethod = typeof(MainWindow).GetMethod("ApplyCompactMode", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Compact-mode method was not found.");
        compactMethod.Invoke(window, [false, false]);
        ValidateInitializedControls(window);
        ValidateBundledArtwork(window, outputDirectory);
        var renders = new List<RenderAudit>
        {
            Render(window, 1420, 900, Path.Combine(outputDirectory, "normal.png")),
            RenderWorkspaceTab(window, "DeckAnalyzerTab", "deck-analyzer.png", outputDirectory),
            RenderWorkspaceTab(window, "LiveDuelTab", "live-duel.png", outputDirectory),
            RenderOpenInspector(window, outputDirectory),
            RenderWorkspaceTab(window, "OwnedOptimizerTab", "owned-card-optimizer.png", outputDirectory),
            RenderArtworkGallery(window, outputDirectory),
            RenderCampaignPlan(window, outputDirectory)
        };
        RenderDeckLibrary(window, outputDirectory, renders);

        compactMethod.Invoke(window, [true, false]);
        ValidateCompactWorkspace(window);
        SeedCompactRoutes(window);
        renders.Add(Render(window, 272, 1002, Path.Combine(outputDirectory, "compact.png")));
        var compactGrid = (DataGrid)window.FindName("CompactAdviceGrid");
        var examples = compactGrid.ItemsSource.Cast<object>().ToArray();
        compactGrid.ItemsSource = Enumerable.Range(0, 30).SelectMany(_ => examples).ToArray();
        renders.Add(Render(window, 272, 1002, Path.Combine(outputDirectory, "compact-scroll-stress.png")));
        VerifyCompactColumnFit(compactGrid, outputDirectory);
        var root = (Grid)window.FindName("RootLayout");
        root.MaxWidth = 246;
        renders.Add(Render(window, 272, 1002, Path.Combine(outputDirectory, "compact-narrow-client.png")));
        VerifyCompactColumnFit(compactGrid, outputDirectory, "compact-narrow-column-fit.json");
        root.MaxWidth = double.PositiveInfinity;
        File.WriteAllText(
            Path.Combine(outputDirectory, "ui-render-audit.json"),
            JsonSerializer.Serialize(renders, JsonOptions));
        window.Close();
        return 0;
    }

    private static RenderAudit RenderArtworkGallery(MainWindow window, string outputDirectory)
    {
        var tabs = (TabControl)window.FindName("WorkspaceTabs");
        tabs.SelectedItem = window.FindName("OwnedOptimizerTab");
        window.UpdateLayout();
        var gallery = FindLogicalDescendants<ListBox>(window).Single(c => c.Name == "OwnedCardsGallery");
        gallery.BringIntoView();
        window.UpdateLayout();
        return Render(window, 1420, 900, Path.Combine(outputDirectory, "automatic-card-artwork.png"));
    }

    private static void RenderDeckLibrary(MainWindow main, string outputDirectory, List<RenderAudit> renders)
    {
        var catalog = FusionCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "yfm.db"));
        var builds = CampaignDeckLibrary.ForCatalog(catalog);
        var rows = ((System.Collections.IEnumerable)typeof(MainWindow).GetField("_ownedCardRows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!).Cast<object>().ToArray();
        var images = rows.ToDictionary(r => ((Card)r.GetType().GetProperty("Card")!.GetValue(r)!).Id,
            r => (ImageSource?)r.GetType().GetProperty("Artwork")!.GetValue(r));
        var inventory = builds[0].Entries.Select(e => new OwnedCardQuantity(e.CardId, e.Copies)).ToArray();
        var available = true;
        CampaignDeckBlueprint? chosen = null;
        var window = new CampaignDeckLibraryWindow(catalog, () => inventory,
            id => images[id],
            () => "MANUAL COLLECTION • isolated rendering fixture", b => chosen = b, () => available);
        renders.Add(Render(window, 1060, 900, Path.Combine(outputDirectory, "recommended-decks.png")));
        var tiles = (ItemsControl)window.FindName("BuildTiles");
        if (tiles.Items.Count != 6) throw new InvalidDataException("Missing deck tiles.");
        if (!((TextBlock)window.FindName("OwnedSummary")).Text.StartsWith("40 / 40", StringComparison.Ordinal))
            throw new InvalidDataException("Required-copy ownership count did not match fixture.");
        var tileObjects = tiles.Items.Cast<object>().ToArray();
        if (tileObjects.Any(t => t.GetType().GetProperty("Artwork")!.GetValue(t) is not BitmapSource))
            throw new InvalidDataException("Deck icons did not decode offline.");
        var select = typeof(CampaignDeckLibraryWindow).GetMethod("SelectBuild", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var check = (Button)window.FindName("AnalyzeReferenceButton");
        var cancel = (Button)window.FindName("CancelReferenceButton");
        check.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => !cancel.IsEnabled, TimeSpan.FromSeconds(20));
        if (!((TextBlock)window.FindName("ReferenceMetrics")).Text.StartsWith("Exact 658", StringComparison.Ordinal))
            throw new InvalidDataException("Reference check did not complete its exact hand analysis.");
        renders.Add(Render(window, 1060, 900, Path.Combine(outputDirectory, "recommended-decks-exact-metrics.png")));
        select.Invoke(window, [builds.Single(b => b.Id == "mercury-control")]);
        check.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => !cancel.IsEnabled, TimeSpan.FromSeconds(10));
        if (!((TextBlock)window.FindName("ReferenceMetrics")).Text.StartsWith("Check cancelled", StringComparison.Ordinal))
            throw new InvalidDataException("Reference check did not cancel cleanly.");
        select.Invoke(window, [builds.Single(b => b.Id == "sand-mercury")]);
        renders.Add(Render(window, 720, 800, Path.Combine(outputDirectory, "recommended-decks-narrow.png")));
        inventory = [];
        window.RefreshInventory();
        if (!((TextBlock)window.FindName("OwnedSummary")).Text.StartsWith("0 / 40", StringComparison.Ordinal))
            throw new InvalidDataException("Live ownership refresh did not clear counts.");
        available = false;
        window.RefreshInventory();
        var use = (Button)window.FindName("UseBuildButton");
        if (use.IsEnabled) throw new InvalidDataException("Active job allowed strategy replacement.");
        available = true;
        window.RefreshInventory();
        var detail = (ScrollViewer)window.FindName("BuildDetailScroll");
        detail.ScrollToEnd();
        window.UpdateLayout();
        if (detail.ScrollableWidth > .1 || detail.ScrollableHeight <= 0)
            throw new InvalidDataException("Deck details did not fit/scroll vertically.");
        var cards = (DataGrid)window.FindName("BuildCards");
        if (cards.Columns[0].ActualWidth < 145 || cards.Columns[4].ActualWidth < 190 || cards.Columns[5].ActualWidth < 125)
            throw new InvalidDataException("Card names, farming guidance, or roles collapsed inside deck details.");
        var missingRows = cards.Items.Cast<object>().ToArray();
        if (missingRows.Any(row => string.IsNullOrWhiteSpace(row.GetType().GetProperty("BestFarm")!.GetValue(row)?.ToString())))
            throw new InvalidDataException("A recommended-deck card omitted its farming guidance.");
        renders.Add(Render(window, 720, 800, Path.Combine(outputDirectory, "recommended-decks-missing-cards.png")));
        var libraryTabs = (TabControl)window.FindName("LibraryTabs");
        libraryTabs.SelectedItem = window.FindName("FreeDuelTab");
        window.UpdateLayout();
        var gallery = (ItemsControl)window.FindName("DuelistGallery");
        if (gallery.Items.Count != 39) throw new InvalidDataException("Free Duel gallery did not contain all 39 opponents.");
        renders.Add(Render(window, 1060, 900, Path.Combine(outputDirectory, "free-duel-gallery.png")));
        var freeDuel = FreeDuelReferenceData.LoadBundled(AppContext.BaseDirectory, catalog);
        typeof(CampaignDeckLibraryWindow).GetMethod("ShowDuelist", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, [freeDuel.Duelists.Single(d => d.Id == 29)]);
        var rewardTabs = (TabControl)window.FindName("DuelistRewardTabs");
        if (rewardTabs.Items.Count != 3 || rewardTabs.Items.Cast<TabItem>().Any(tab => ((DataGrid)tab.Content).Items.Count == 0))
            throw new InvalidDataException("Duelist popup did not contain all three complete reward tables.");
        renders.Add(Render(window, 1060, 900, Path.Combine(outputDirectory, "free-duel-duelist-popup.png")));
        typeof(CampaignDeckLibraryWindow).GetMethod("ShowCard", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, [catalog.GetCard(713), null]);
        var dropSources = (DataGrid)window.FindName("CardDropSources");
        if (dropSources.Items.Count == 0 || string.IsNullOrWhiteSpace(((TextBlock)window.FindName("CardDetailShop")).Text))
            throw new InvalidDataException("Card popup omitted drop sources or password-shop status.");
        renders.Add(Render(window, 1060, 900, Path.Combine(outputDirectory, "free-duel-card-popup.png")));
        typeof(CampaignDeckLibraryWindow).GetMethod("CloseOverlay", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
        var search = (TextBox)window.FindName("ReferenceSearchBox");
        search.Text = "met";
        window.UpdateLayout();
        var searchResults = (ItemsControl)window.FindName("SearchResults");
        if (searchResults.Items.Count == 0)
            throw new InvalidDataException("Three-letter search did not produce card or duelist predictions.");
        search.Text = "me";
        if (searchResults.Items.Count != 0)
            throw new InvalidDataException("Search predictions appeared before the third letter.");
        libraryTabs.SelectedItem = window.FindName("RecommendedDecksTab");
        typeof(CampaignDeckLibraryWindow).GetMethod("UseBuild_Click", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [use, new RoutedEventArgs()]);
        if (chosen?.Id != "sand-mercury") throw new InvalidDataException("Adapt button did not select the displayed strategy.");
        File.WriteAllText(Path.Combine(outputDirectory, "deck-library-ui-audit.json"), JsonSerializer.Serialize(new
        {
            DeckTiles = tiles.Items.Count,
            UniqueHeroIcons = builds.Select(b => b.HeroCardId).Distinct().Count(),
            RequiredCopyCount = true,
            LiveRefresh = true,
            MissingCardDetails = true,
            BusyStrategyProtected = true,
            ExactReferenceCheck = true,
            ReferenceCancellation = true,
            VerticalScrolling = true,
            FreeDuelOpponents = gallery.Items.Count,
            RewardTablesPerDuelist = rewardTabs.Items.Count,
            CardDropCrossLinks = dropSources.Items.Count,
            ThreeLetterSearch = true,
            AdaptStrategySelected = chosen.Id
        }, JsonOptions));
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > timeout) throw new TimeoutException("Deck library UI operation did not finish.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }

    private static void ValidateBundledArtwork(MainWindow window, string outputDirectory)
    {
        var rows = (System.Collections.IEnumerable)typeof(MainWindow).GetField("_ownedCardRows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var count = 0;
        foreach (var row in rows)
        {
            if (row.GetType().GetProperty("Artwork")!.GetValue(row) is not BitmapSource bitmap || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
                throw new InvalidOperationException("A bundled card image did not decode through the production row/cache.");
            count++;
        }
        if (count != 722) throw new InvalidOperationException($"Expected all 722 automatic images, got {count}.");
        File.WriteAllText(Path.Combine(outputDirectory, "artwork-audit.json"), JsonSerializer.Serialize(new { AutomaticCardsDecoded = count, SettingsRequired = false }, JsonOptions));
    }

    private static RenderAudit RenderWorkspaceTab(
        MainWindow window,
        string tabName,
        string fileName,
        string outputDirectory)
    {
        var tabs = window.FindName("WorkspaceTabs") as TabControl
            ?? throw new InvalidOperationException("Workspace tab control was not found.");
        tabs.SelectedItem = window.FindName(tabName) as TabItem
            ?? throw new InvalidOperationException($"Workspace tab {tabName} was not found.");
        if (tabName == "OwnedOptimizerTab")
        {
            var workers = FindLogicalDescendants<ComboBox>(window).Single(c => c.Name == "CpuWorkersCombo");
            if (workers.Items.Count != DeckBuildJob.MaximumWorkerCount + 1 || workers.SelectedValue is not int)
                throw new InvalidOperationException("CPU worker selector did not initialize Auto and manual choices.");
            var autoLabel = workers.Items[0].GetType().GetProperty("Label")!.GetValue(workers.Items[0])?.ToString();
            if (autoLabel != $"Auto • {DeckBuildJob.AutoSearchWorkerCount} search / {DeckBuildJob.AutoAnalysisWorkerCount} analysis")
                throw new InvalidOperationException("CPU worker selector does not show the split Auto counts.");
            var rows = (System.Collections.IEnumerable)typeof(MainWindow).GetField("_ownedCardRows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            foreach (var row in rows.Cast<object>().Take(8)) row.GetType().GetProperty("Quantity")!.SetValue(row, 1);
            var gallery = FindLogicalDescendants<ListBox>(window).Single(c => c.Name == "OwnedCardsGallery");
            gallery.BringIntoView();
            var advanced = FindLogicalDescendants<Expander>(window).Single(e => e.Header?.ToString() == "ADVANCED STRATEGY SETTINGS");
            advanced.IsExpanded = true;
        }
        return Render(window, 1420, 900, Path.Combine(outputDirectory, fileName));
    }

    private static void ValidateInitializedControls(MainWindow window)
    {
        RequireChildCount(window, "HandPickerPanel", 5);
        RequireChildCount(window, "MonsterPickerPanel", 5);
        RequireChildCount(window, "SpellPickerPanel", 5);
        RequireChildCount(window, "DeckPickerPanel", 40);
        var ownedCards = window.FindName("OwnedCardsGallery") as ListBox
            ?? throw new InvalidOperationException("Owned-card grid was not found.");
        var ownedRows = typeof(MainWindow).GetField("_ownedCardRows", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as System.Collections.ICollection
            ?? throw new InvalidOperationException("Owned-card source rows were not found.");
        if (ownedRows.Count != 722) throw new InvalidOperationException($"Expected 722 owned-card source rows; found {ownedRows.Count}.");

        var timerField = typeof(MainWindow).GetField("_liveTimer", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Live update timer was not found.");
        var timer = timerField.GetValue(window) as DispatcherTimer
            ?? throw new InvalidOperationException("Live update timer was not a dispatcher timer.");
        if (timer.Interval != TimeSpan.FromSeconds(1))
        {
            throw new InvalidOperationException($"Expected a one-second live update interval; found {timer.Interval}.");
        }

        if (window.FindName("LiveConnectButton") is not null || window.FindName("LiveAutoCheckBox") is not null)
        {
            throw new InvalidOperationException("A redundant manual live-refresh control is still present.");
        }

        RequireButtonContent(window, "LoadCurrentDeckButton", "LOAD CURRENT DECK FROM SAVE");
        RequireButtonContent(window, "RefreshSaveButton", "REFRESH");
        RequireText(window, "LiveUpdateHealthText", "ERROR");
        ValidateButtonPalette(window);
        ValidateTabPalette(window);
        ValidateLiveHealthTransitions(window);
        ValidateInspectorInteraction(window);
        ValidateCampaignOptimizerControls(window);
    }

    private static void RequireButtonContent(MainWindow window, string name, string expected)
    {
        var button = window.FindName(name) as Button
            ?? throw new InvalidOperationException($"Button {name} was not found.");
        if (!string.Equals(button.Content?.ToString(), expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Button {name} did not have the expected explanatory label.");
        }
    }

    private static void RequireText(MainWindow window, string name, string expected)
    {
        var textBlock = window.FindName(name) as TextBlock
            ?? throw new InvalidOperationException($"Text block {name} was not found.");
        if (!string.Equals(textBlock.Text, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Text block {name} did not have the expected initial state.");
        }
    }

    private static void ValidateButtonPalette(MainWindow window)
    {
        var buttons = FindLogicalDescendants<Button>(window).ToArray();
        if (buttons.Length < 10)
        {
            throw new InvalidOperationException($"Expected at least ten application buttons; found {buttons.Length}.");
        }

        foreach (var button in buttons)
        {
            if (button.Background is not SolidColorBrush background ||
                background.Color.B < background.Color.R ||
                background.Color.B < background.Color.G)
            {
                throw new InvalidOperationException($"Button '{button.Content}' does not use a blue background.");
            }

            if (button.Foreground is not SolidColorBrush foreground ||
                foreground.Color.R < 220 ||
                foreground.Color.G < 220 ||
                foreground.Color.B < 220)
            {
                throw new InvalidOperationException($"Button '{button.Content}' does not use light text.");
            }
        }

        foreach (var resourceName in new[] { "ButtonBrush", "ButtonHoverBrush", "ButtonPressedBrush", "ButtonDisabledBrush" })
        {
            if (Application.Current.FindResource(resourceName) is not SolidColorBrush brush ||
                brush.Color.B < brush.Color.R ||
                brush.Color.B < brush.Color.G)
            {
                throw new InvalidOperationException($"Button state resource {resourceName} is not blue.");
            }
        }
    }

    private static void ValidateTabPalette(MainWindow window)
    {
        var tabs = FindLogicalDescendants<TabItem>(window).ToArray();
        if (tabs.Length < 4)
        {
            throw new InvalidOperationException($"Expected at least four application tabs; found {tabs.Length}.");
        }

        foreach (var tab in tabs)
        {
            if (tab.Background is not SolidColorBrush background ||
                background.Color.B < background.Color.R ||
                background.Color.B < background.Color.G)
            {
                throw new InvalidOperationException($"Tab '{tab.Header}' does not use a blue background.");
            }

            if (tab.Foreground is not SolidColorBrush foreground ||
                foreground.Color.R < 220 ||
                foreground.Color.G < 220 ||
                foreground.Color.B < 220)
            {
                throw new InvalidOperationException($"Tab '{tab.Header}' does not use light text.");
            }
        }
    }

    private static void ValidateLiveHealthTransitions(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod("SetLiveHealth", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Live health method was not found.");
        method.Invoke(window, [true]);
        RequireText(window, "LiveUpdateHealthText", "UP TO DATE");
        method.Invoke(window, [false]);
        RequireText(window, "LiveUpdateHealthText", "ERROR");
    }

    private static void ValidateCampaignOptimizerControls(MainWindow window)
    {
        var scope = window.FindName("CampaignScopeCombo") as ComboBox
            ?? throw new InvalidOperationException("Campaign-goal selector was not found.");
        var opponent = window.FindName("CampaignOpponentCombo") as ComboBox
            ?? throw new InvalidOperationException("Campaign-opponent selector was not found.");
        var opponentHint = window.FindName("CampaignOpponentHint") as TextBlock
            ?? throw new InvalidOperationException("Campaign-opponent hint was not found.");
        var starChips = window.FindName("UseSavedStarChipsCheckBox") as CheckBox
            ?? throw new InvalidOperationException("Saved-Star-Chip setting was not found.");
        var planPanel = window.FindName("CampaignPlanPanel") as Border
            ?? throw new InvalidOperationException("Campaign-result panel was not found.");
        if (scope.Items.Count != 4 || opponent.Items.Count != 39 || scope.SelectedIndex != 0)
        {
            throw new InvalidOperationException("Campaign optimizer controls did not initialize their expected scope or opponent choices.");
        }

        if (opponent.Visibility != Visibility.Collapsed || opponentHint.Visibility != Visibility.Visible)
        {
            throw new InvalidOperationException("The target-duelist selector should remain hidden until opponent-specific mode is selected.");
        }

        scope.SelectedIndex = 1;
        window.UpdateLayout();
        if (opponent.Visibility != Visibility.Visible || !opponent.IsEnabled || opponentHint.Visibility != Visibility.Collapsed)
        {
            throw new InvalidOperationException("Opponent-specific campaign mode did not expose an enabled duelist selector.");
        }

        scope.SelectedIndex = 0;
        window.UpdateLayout();
        if (opponent.Visibility != Visibility.Collapsed || planPanel.Visibility != Visibility.Collapsed)
        {
            throw new InvalidOperationException("Campaign controls did not restore the compact general-purpose configuration.");
        }

        if (starChips.IsChecked == true && !starChips.IsEnabled)
        {
            throw new InvalidOperationException("Saved Star Chips cannot be selected when no saved budget is available.");
        }
    }

    private static void VerifyCompactColumnFit(DataGrid grid, string outputDirectory, string evidenceName = "compact-column-fit.json")
    {
        var viewer = VisualDescendants(grid).OfType<ScrollViewer>().First();
        if (viewer.ScrollableWidth > .5 || viewer.ScrollableHeight <= 0)
            throw new InvalidOperationException($"Compact columns must fit while vertical scrolling is active: horizontal={viewer.ScrollableWidth}.");
        var names = VisualDescendants(grid).OfType<TextBlock>().Where(t => t.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path?.Path == "Result").ToArray();
        if (names.Length == 0 || names.Any(t => t.ActualHeight > 30.5))
            throw new InvalidOperationException("Compact card names exceed two lines or were not rendered.");
        if (grid.Columns[0].ActualWidth < 80 || grid.Columns[0].ActualWidth > 115.5 || grid.Columns.Sum(c => c.ActualWidth) > viewer.ViewportWidth + .5)
            throw new InvalidOperationException("Compact columns exceed the available viewport.");
        File.WriteAllText(Path.Combine(outputDirectory, evidenceName), JsonSerializer.Serialize(new
        {
            HorizontalScrollWidth = viewer.ScrollableWidth,
            VerticalScrollHeight = viewer.ScrollableHeight,
            ResultColumnWidth = grid.Columns[0].ActualWidth,
            ViewportWidth = viewer.ViewportWidth,
            NameHeights = names.Select(t => t.ActualHeight).ToArray()
        }, JsonOptions));
    }

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in VisualDescendants(child)) yield return descendant;
        }
    }

    private static void ValidateCompactWorkspace(MainWindow window)
    {
        if (window.MinWidth != 272 || window.MaxWidth != 272 || window.MinHeight != 1002 || window.MaxHeight != 1002)
        {
            throw new InvalidOperationException("Compact Live must stay bounded to the exact 272x1002 window dimensions.");
        }

        if (window.FindName("AppHeader") is not FrameworkElement header || header.Visibility != Visibility.Collapsed)
        {
            throw new InvalidOperationException("The full application header remains visible in Compact Live.");
        }

        foreach (var removedControl in new[]
                 {
                     "CompactConnectionText", "CompactPlayerLpText", "CompactOpponentLpText",
                     "CompactTerrainText", "CompactUpdateHealthText", "CompactHandGrid", "CompactAdviceSummary"
                 })
        {
            if (window.FindName(removedControl) is not null)
            {
                throw new InvalidOperationException($"Compact Live still contains excluded control {removedControl}.");
            }
        }

        var adviceGrid = window.FindName("CompactAdviceGrid") as DataGrid
            ?? throw new InvalidOperationException("Compact route grid was not found.");
        var headers = adviceGrid.Columns.Select(column => column.Header?.ToString()).ToArray();
        if (!headers.SequenceEqual(["RESULT", "ATK", "ROUTE"], StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Compact Live must show only RESULT, ATK, and ROUTE columns.");
        }

        var templateColumn = adviceGrid.Columns[2] as DataGridTemplateColumn
            ?? throw new InvalidOperationException("Compact guardian information must remain inside the route column.");
        if (templateColumn.CellTemplate is null)
        {
            throw new InvalidOperationException("Compact route template is missing.");
        }
    }

    private static void SeedCompactRoutes(MainWindow window)
    {
        var adviceGrid = window.FindName("CompactAdviceGrid") as DataGrid
            ?? throw new InvalidOperationException("Compact route grid was not found.");
        adviceGrid.ItemsSource = new[]
        {
            new { Result = "Twin-headed Thunder Dragon", Attack = 2800, Route = "1+2+3", GuardianStar1 = "☉ > ☾ > ♀", GuardianOutcomes1 = "F1✓ F2?", GuardianStar2 = "♂ > ♃ > ♄", GuardianOutcomes2 = "F1= F2?", GuardianVisual1 = "Warm", GuardianVisual2 = "Threat" },
            new { Result = "Pumpking the King of Ghosts", Attack = 1800, Route = "F(3)+2+5", GuardianStar1 = "☾ > ♀ > ☿", GuardianOutcomes1 = "F3?", GuardianStar2 = "♂ > ♃ > ♄", GuardianOutcomes2 = "F3?", GuardianVisual1 = "Warm", GuardianVisual2 = "Warm" },
            new { Result = "Armored Zombie", Attack = 1500, Route = "F(8)+4", GuardianStar1 = "☉ > ☾ > ♀", GuardianOutcomes1 = "—", GuardianStar2 = "☾ > ♀ > ☿", GuardianOutcomes2 = "—", GuardianVisual1 = "Threat", GuardianVisual2 = "Threat" }
        };
    }

    private static void ValidateInspectorInteraction(MainWindow window)
    {
        var panel = window.FindName("InspectorPanel") as Border
            ?? throw new InvalidOperationException("The optional card inspector panel was not found.");
        var button = window.FindName("InspectorToggleButton") as Button
            ?? throw new InvalidOperationException("The inspector toggle button was not found.");
        var tabs = window.FindName("WorkspaceTabs") as TabControl
            ?? throw new InvalidOperationException("Workspace tabs were not found.");
        var liveTab = window.FindName("LiveDuelTab") as TabItem
            ?? throw new InvalidOperationException("Live Duel tab was not found.");
        var openMethod = typeof(MainWindow).GetMethod("OpenInspector", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Inspector open method was not found.");
        var closeMethod = typeof(MainWindow).GetMethod("CloseInspector", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Inspector close method was not found.");

        if (panel.Visibility != Visibility.Collapsed || panel.Width != 360)
        {
            throw new InvalidOperationException("The card inspector must start closed at its expected overlay width.");
        }

        tabs.SelectedItem = liveTab;
        openMethod.Invoke(window, null);
        if (panel.Visibility != Visibility.Visible || !string.Equals(button.Content?.ToString(), "CLOSE INSPECTOR", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Opening the card inspector did not expose the overlay and close control.");
        }

        closeMethod.Invoke(window, [true]);
        if (panel.Visibility != Visibility.Collapsed || !string.Equals(button.Content?.ToString(), "INSPECT CARDS", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Closing the card inspector did not restore the unobstructed Live Duel workspace.");
        }
    }

    private static RenderAudit RenderOpenInspector(MainWindow window, string outputDirectory)
    {
        var tabs = window.FindName("WorkspaceTabs") as TabControl
            ?? throw new InvalidOperationException("Workspace tabs were not found.");
        tabs.SelectedItem = window.FindName("LiveDuelTab") as TabItem
            ?? throw new InvalidOperationException("Live Duel tab was not found.");
        var openMethod = typeof(MainWindow).GetMethod("OpenInspector", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Inspector open method was not found.");
        openMethod.Invoke(window, null);
        var transform = window.FindName("InspectorTranslate") as TranslateTransform
            ?? throw new InvalidOperationException("Inspector slide transform was not found.");
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = 0;
        var result = Render(window, 1420, 900, Path.Combine(outputDirectory, "live-duel-inspector.png"));
        var closeMethod = typeof(MainWindow).GetMethod("CloseInspector", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Inspector close method was not found.");
        closeMethod.Invoke(window, [true]);
        return result;
    }

    private static RenderAudit RenderCampaignPlan(MainWindow window, string outputDirectory)
    {
        var catalog = typeof(MainWindow).GetField("_catalog", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as FusionCatalog
            ?? throw new InvalidOperationException("Campaign-plan render could not access the loaded card catalog.");
        var research = typeof(MainWindow).GetField("_campaignResearchData", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as CampaignResearchData
            ?? throw new InvalidOperationException("Campaign-plan render could not access the validated research data.");
        var owned = Enumerable.Range(1, 13).Select(cardId => new OwnedCardQuantity(cardId, 3)).ToArray();
        SeedOwnedInventory(window, owned);
        var plan = new CampaignDeckOptimizer(catalog, research).Optimize(
            owned,
            starChips: 70,
            useStarChips: true,
            CampaignOpponentScope.GeneralSafety,
            options: new DeckOptimizationOptions(
                SampleHands: 2,
                ExactFinalists: 1,
                IncludeGlitches: false,
                Profile: DeckStrategyProfile.ControlAndSafety));
        var showReport = typeof(MainWindow).GetMethod("ShowOptimizationReport", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Campaign-plan report presenter was not found.");
        var activeContext = typeof(MainWindow).GetField("_activeCampaignContext", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Campaign context field was not found.");
        activeContext.SetValue(window, plan.Context);
        showReport.Invoke(window, [plan.DeckPlan.ResultingDeck]);
        var showBest = typeof(MainWindow).GetMethod("ShowBestSoFar", BindingFlags.Instance | BindingFlags.NonPublic)!;
        showBest.Invoke(window, [new DeckBuildCandidate(plan.DeckPlan.ResultingDeck, 0), DeckBuildState.Completed, false]);
        ValidateSuggestedDeckSorting(window, plan.DeckPlan.ResultingDeck, outputDirectory);
        var supportStatus = (TextBlock)window.FindName("OptimizationStatus");
        if (!supportStatus.Text.Contains("setup available") || !supportStatus.Text.Contains("board clear drawn") ||
            !supportStatus.Text.Contains("separate turns"))
            throw new InvalidOperationException("Installed results lost the guide support metrics or setup limitation.");

        var panel = window.FindName("CampaignPlanPanel") as Border
            ?? throw new InvalidOperationException("Campaign-result panel was not found.");
        var threats = window.FindName("CampaignThreatsGrid") as DataGrid
            ?? throw new InvalidOperationException("Campaign-threat grid was not found.");
        var purchases = window.FindName("PurchasePlanGrid") as DataGrid
            ?? throw new InvalidOperationException("Purchase-plan grid was not found.");
        if (panel.Visibility != Visibility.Visible || threats.Items.Count == 0 || purchases.Items.Count == 0)
        {
            throw new InvalidOperationException("Campaign-plan presentation did not expose its required summary, threat, and purchase surfaces.");
        }

        var optimizerTabs = FindLogicalDescendants<TabControl>(window)
            .Single(control => control.Items.Cast<object>()
                .OfType<TabItem>()
                .Any(item => Equals(item.Header, "STAR CHIPS")));
        optimizerTabs.SelectedItem = optimizerTabs.Items.Cast<TabItem>().Single(item => Equals(item.Header, "STAR CHIPS"));
        var workspaceTabs = window.FindName("WorkspaceTabs") as TabControl
            ?? throw new InvalidOperationException("Workspace tabs were not found.");
        workspaceTabs.SelectedItem = window.FindName("OwnedOptimizerTab") as TabItem;

        var result = Render(window, 1420, 900, Path.Combine(outputDirectory, "campaign-plan.png"));
        var reset = typeof(MainWindow).GetMethod("ResetOptimizerResults", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Optimizer reset method was not found.");
        reset.Invoke(window, null);
        return result;
    }

    private static void ValidateSuggestedDeckSorting(MainWindow window, DeckOptimizationReport report, string outputDirectory)
    {
        var combo = FindLogicalDescendants<ComboBox>(window).Single(c => c.Name == "SuggestedDeckSortCombo");
        var grid = (DataGrid)window.FindName("OptimizedDeckGrid");
        var reportOrder = report.Deck.Select(e => e.Card.Id).ToArray();
        if (!combo.Items.Cast<string>().SequenceEqual(["Alphabetical", "Card number", "ATK", "DEF"]))
            throw new InvalidDataException("Suggested-deck order choices changed.");
        if (combo.SelectedItem as string != "Alphabetical") throw new InvalidDataException("Suggested-deck default is not alphabetical.");
        var showReport = typeof(MainWindow).GetMethod("ShowOptimizationReport", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var order in combo.Items.Cast<string>())
        {
            combo.SelectedItem = order;
            var expected = order switch
            {
                "Card number" => report.Deck.Select(e => e.Card).OrderBy(c => c.Id).ToArray(),
                "ATK" => report.Deck.Select(e => e.Card).OrderByDescending(c => c.Attack).ThenBy(c => c.Name).ThenBy(c => c.Id).ToArray(),
                "DEF" => report.Deck.Select(e => e.Card).OrderByDescending(c => c.Defense).ThenBy(c => c.Name).ThenBy(c => c.Id).ToArray(),
                _ => report.Deck.Select(e => e.Card).OrderBy(c => c.Name).ThenBy(c => c.Id).ToArray()
            };
            int[] VisibleIds() => grid.Items.Cast<object>().Select(row => ((Card)row.GetType().GetProperty("Card")!.GetValue(row)!).Id).ToArray();
            if (!VisibleIds().SequenceEqual(expected.Select(c => c.Id))) throw new InvalidDataException($"Suggested-deck {order} sorting failed.");
            showReport.Invoke(window, [report]);
            if (!VisibleIds().SequenceEqual(expected.Select(c => c.Id))) throw new InvalidDataException($"A new result reset {order} sorting.");
            if (!report.Deck.Select(e => e.Card.Id).SequenceEqual(reportOrder)) throw new InvalidDataException("Display sorting changed the optimizer report.");
            var settingsType = typeof(MainWindow).Assembly.GetType("YfmCompanion.Desktop.DesktopSettingsStore")!;
            var settings = settingsType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [null])!;
            if ((string?)settings.GetType().GetProperty("SuggestedDeckSort")!.GetValue(settings) != order)
                throw new InvalidDataException("Suggested-deck order was not saved.");
            var resultTabs = FindLogicalDescendants<TabControl>(window).Single(c => c.Items.Cast<object>()
                .OfType<TabItem>().Any(t => t.Header?.ToString() == "DECK"));
            resultTabs.SelectedIndex = 0;
            Render(window, 1420, 900, Path.Combine(outputDirectory, "suggested-order-" + order.Replace(' ', '-') + ".png"));
        }
        combo.SelectedItem = "Alphabetical";
        File.WriteAllText(Path.Combine(outputDirectory, "suggested-deck-sorting.json"), JsonSerializer.Serialize(new
        { Default = "Alphabetical", Orders = combo.Items.Cast<string>().ToArray(), NewResultRetainsOrder = true, PreferenceSaved = true, ReportUnchanged = true }, JsonOptions));
    }

    private static void SeedOwnedInventory(MainWindow window, IEnumerable<OwnedCardQuantity> owned)
    {
        var quantities = owned.ToDictionary(entry => entry.CardId, entry => entry.Quantity);
        var grid = window.FindName("OwnedCardsGallery") as ListBox
            ?? throw new InvalidOperationException("Owned-card grid was not found.");
        var sourceRows = typeof(MainWindow).GetField("_ownedCardRows", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as System.Collections.IEnumerable
            ?? throw new InvalidOperationException("Owned-card source rows were not found.");
        foreach (var row in sourceRows)
        {
            var cardProperty = row.GetType().GetProperty("Card")
                ?? throw new InvalidOperationException("Owned-card row card property was not found.");
            var card = cardProperty.GetValue(row) as Card
                ?? throw new InvalidOperationException("Owned-card row did not provide a card.");
            var quantityProperty = row.GetType().GetProperty("Quantity")
                ?? throw new InvalidOperationException("Owned-card row quantity property was not found.");
            quantityProperty.SetValue(row, quantities.GetValueOrDefault(card.Id));
        }

        var refresh = typeof(MainWindow).GetMethod("RefreshOwnedGallery", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Owned-card gallery refresh method was not found.");
        refresh.Invoke(window, null);
    }

    private static IEnumerable<T> FindLogicalDescendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is T match)
            {
                yield return match;
            }

            if (child is DependencyObject dependencyObject)
            {
                foreach (var descendant in FindLogicalDescendants<T>(dependencyObject))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static void RequireChildCount(MainWindow window, string name, int expected)
    {
        var panel = window.FindName(name) as Panel
            ?? throw new InvalidOperationException($"Panel {name} was not found.");
        if (panel.Children.Count != expected)
        {
            throw new InvalidOperationException($"Expected {expected} controls in {name}; found {panel.Children.Count}.");
        }
    }

    private static RenderAudit Render(Window window, int width, int height, string outputPath)
    {
        window.Width = width;
        window.Height = height;
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        var colors = new HashSet<int>();
        var visiblePixels = 0;
        var brightPixels = 0;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var blue = pixels[offset];
            var green = pixels[offset + 1];
            var red = pixels[offset + 2];
            var alpha = pixels[offset + 3];
            if (alpha == 0)
            {
                continue;
            }

            visiblePixels++;
            if (red + green + blue >= 450)
            {
                brightPixels++;
            }

            if (colors.Count < 10_000)
            {
                colors.Add((alpha << 24) | (red << 16) | (green << 8) | blue);
            }
        }

        if (visiblePixels < width * height / 2 || brightPixels < 500 || colors.Count < 32)
        {
            throw new InvalidOperationException($"Rendered UI appears blank or incomplete: {visiblePixels} visible pixels, {brightPixels} bright pixels, {colors.Count} colors.");
        }

        return new RenderAudit(
            Path.GetFileName(outputPath),
            width,
            height,
            visiblePixels,
            brightPixels,
            colors.Count);
    }

    private sealed record RenderAudit(
        string File,
        int Width,
        int Height,
        int VisiblePixels,
        int BrightPixels,
        int DistinctColors);
}
