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

        var window = new MainWindow();
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
        var renders = new List<RenderAudit>
        {
            Render(window, 1420, 900, Path.Combine(outputDirectory, "normal.png")),
            RenderWorkspaceTab(window, "DeckAnalyzerTab", "deck-analyzer.png", outputDirectory),
            RenderWorkspaceTab(window, "LiveDuelTab", "live-duel.png", outputDirectory),
            RenderOpenInspector(window, outputDirectory),
            RenderWorkspaceTab(window, "SaveSyncTab", "save-snapshot.png", outputDirectory),
            RenderWorkspaceTab(window, "OwnedOptimizerTab", "owned-card-optimizer.png", outputDirectory),
            RenderCampaignPlan(window, outputDirectory)
        };

        compactMethod.Invoke(window, [true, false]);
        ValidateCompactWorkspace(window);
        SeedCompactRoutes(window);
        renders.Add(Render(window, 320, 1040, Path.Combine(outputDirectory, "compact.png")));
        File.WriteAllText(
            Path.Combine(outputDirectory, "ui-render-audit.json"),
            JsonSerializer.Serialize(renders, JsonOptions));
        window.Close();
        return 0;
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
        return Render(window, 1420, 900, Path.Combine(outputDirectory, fileName));
    }

    private static void ValidateInitializedControls(MainWindow window)
    {
        RequireChildCount(window, "HandPickerPanel", 5);
        RequireChildCount(window, "MonsterPickerPanel", 5);
        RequireChildCount(window, "SpellPickerPanel", 5);
        RequireChildCount(window, "DeckPickerPanel", 40);
        var ownedCards = window.FindName("OwnedCardsGrid") as DataGrid
            ?? throw new InvalidOperationException("Owned-card grid was not found.");
        if (ownedCards.Items.Count != 722)
        {
            throw new InvalidOperationException($"Expected 722 owned-card rows; found {ownedCards.Items.Count}.");
        }

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
        RequireButtonContent(window, "RefreshSaveButton", "REFRESH SAVED SNAPSHOT");
        RequireButtonContent(window, "ApplyDeckButton", "LOAD THIS DECK INTO ANALYZER");
        RequireButtonContent(window, "ApplyOwnedButton", "LOAD OWNED CARDS INTO OPTIMIZER");
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
        if (tabs.Length < 5)
        {
            throw new InvalidOperationException($"Expected at least five application tabs; found {tabs.Length}.");
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

    private static void ValidateCompactWorkspace(MainWindow window)
    {
        if (window.MinWidth != 0 || window.MinHeight != 0)
        {
            throw new InvalidOperationException("Compact Live must not impose a minimum window size.");
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
            throw new InvalidOperationException("Compact guardian route template is missing.");
        }
    }

    private static void SeedCompactRoutes(MainWindow window)
    {
        var adviceGrid = window.FindName("CompactAdviceGrid") as DataGrid
            ?? throw new InvalidOperationException("Compact route grid was not found.");
        adviceGrid.ItemsSource = new[]
        {
            new { Result = "Twin-headed Thunder Dragon", Attack = 2800, Route = "1+2+3", GuardianStar1 = "☉ > ☾ > ♀", GuardianOutcomes1 = "F1✓ F2?", GuardianStar2 = "♂ > ♃ > ♄", GuardianOutcomes2 = "F1= F2?" },
            new { Result = "Pumpking the King of Ghosts", Attack = 1800, Route = "F(3)+2+5", GuardianStar1 = "☾ > ♀ > ☿", GuardianOutcomes1 = "F3?", GuardianStar2 = "♂ > ♃ > ♄", GuardianOutcomes2 = "F3?" },
            new { Result = "Armored Zombie", Attack = 1500, Route = "F(8)+4", GuardianStar1 = "☉ > ☾ > ♀", GuardianOutcomes1 = "—", GuardianStar2 = "☾ > ♀ > ☿", GuardianOutcomes2 = "—" }
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
        var showPlan = typeof(MainWindow).GetMethod("ShowCampaignPlan", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Campaign-plan presenter was not found.");
        showReport.Invoke(window, [plan.DeckPlan.ResultingDeck]);
        showPlan.Invoke(window, [plan]);

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

    private static void SeedOwnedInventory(MainWindow window, IEnumerable<OwnedCardQuantity> owned)
    {
        var quantities = owned.ToDictionary(entry => entry.CardId, entry => entry.Quantity);
        var grid = window.FindName("OwnedCardsGrid") as DataGrid
            ?? throw new InvalidOperationException("Owned-card grid was not found.");
        foreach (var row in grid.Items)
        {
            var cardProperty = row.GetType().GetProperty("Card")
                ?? throw new InvalidOperationException("Owned-card row card property was not found.");
            var card = cardProperty.GetValue(row) as Card
                ?? throw new InvalidOperationException("Owned-card row did not provide a card.");
            var quantityProperty = row.GetType().GetProperty("Quantity")
                ?? throw new InvalidOperationException("Owned-card row quantity property was not found.");
            quantityProperty.SetValue(row, quantities.GetValueOrDefault(card.Id));
        }

        grid.Items.Refresh();
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
