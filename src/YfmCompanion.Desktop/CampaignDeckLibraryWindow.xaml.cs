using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using YfmCompanion.Data;
using YfmCompanion.Engine;

namespace YfmCompanion.Desktop;

public partial class CampaignDeckLibraryWindow : Window
{
    private readonly FusionCatalog _catalog;
    private readonly Func<IEnumerable<OwnedCardQuantity>> _inventory;
    private readonly Func<int, ImageSource?> _artwork;
    private readonly Func<string> _source;
    private readonly Action<CampaignDeckBlueprint> _useBuild;
    private readonly Func<bool> _canUseBuild;
    private readonly IReadOnlyList<CampaignDeckBlueprint> _builds;
    private readonly FreeDuelReferenceData _freeDuel;
    private readonly string _portraitRoot;
    private readonly Dictionary<int, ImageSource> _portraitCache = [];
    private readonly Dictionary<string, DeckAnalysisReport> _analyses = [];
    private CampaignDeckBlueprint _selected;
    private FreeDuelistReference? _lastDuelist;
    private CancellationTokenSource? _analysisCancellation;
    private DispatcherOperation? _queuedRefresh;
    private bool _closed;

    public CampaignDeckLibraryWindow(FusionCatalog catalog, Func<IEnumerable<OwnedCardQuantity>> inventory,
        Func<int, ImageSource?> artwork, Func<string> source, Action<CampaignDeckBlueprint> useBuild, Func<bool> canUseBuild)
    {
        _catalog = catalog;
        _inventory = inventory;
        _artwork = artwork;
        _source = source;
        _useBuild = useBuild;
        _canUseBuild = canUseBuild;
        _builds = CampaignDeckLibrary.ForCatalog(catalog);
        _selected = _builds.First();
        _freeDuel = FreeDuelReferenceData.LoadBundled(AppContext.BaseDirectory, catalog);
        _portraitRoot = Path.Combine(RuntimeResources.FindRoot(AppContext.BaseDirectory), "DuelistPortraits");
        InitializeComponent();
        FreeDuelIntro.Text = $"All {_freeDuel.Duelists.Count} original Free Duel opponents • click a portrait for S/A POW, S/A TEC and B/C/D rewards. {_freeDuel.ProbabilityNote}";
        DuelistGallery.ItemsSource = _freeDuel.Duelists.Select(d => new DuelistTile(d, LoadPortrait(d))).ToArray();
        RefreshInventory();
        SelectBuild(_selected);
        Closed += (_, _) => { _closed = true; _analysisCancellation?.Cancel(); _queuedRefresh?.Abort(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && DetailOverlay.Visibility == Visibility.Visible) CloseOverlay(); };
    }

    public void QueueRefresh()
    {
        if (_closed || _queuedRefresh?.Status == DispatcherOperationStatus.Pending) return;
        _queuedRefresh = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RefreshInventory));
    }

    public void RefreshInventory()
    {
        if (_closed) return;
        var inventory = _inventory().ToArray();
        BuildTiles.ItemsSource = _builds.Select(b => new DeckTile(b, _artwork(b.HeroCardId),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(b.Accent)), $"{b.OwnedCopies(inventory)} / 40 copies owned"));
        CollectionSourceText.Text = _source() + " • Counts match required copies, including cards in your saved deck and chest.";
        RefreshDetailInventory(inventory);
        UseBuildButton.IsEnabled = _canUseBuild();
        AdaptHint.Text = UseBuildButton.IsEnabled
            ? "Uses owned cards as a starting deck; missing cards are replaced. Other strategies are still compared, so the best result can differ."
            : "Finish or stop the active build before changing its starting strategy.";
    }

    private void RefreshDetailInventory(IReadOnlyList<OwnedCardQuantity> inventory)
    {
        var quantities = inventory.GroupBy(e => e.CardId).ToDictionary(g => g.Key, g => g.Sum(e => (long)Math.Max(0, e.Quantity)));
        var owned = _selected.OwnedCopies(inventory);
        OwnedSummary.Text = $"{owned} / 40 required copies owned • {40 - owned} missing copies";
        BuildCards.ItemsSource = _selected.Entries.Select(e =>
        {
            var card = _catalog.GetCard(e.CardId);
            var have = quantities.GetValueOrDefault(e.CardId);
            var missing = Math.Max(0, e.Copies - have);
            var best = _freeDuel.BestFarmForCard(e.CardId);
            var farm = missing == 0 ? "Owned"
                : best is null ? "No Free Duel drop"
                : $"{best.DuelistName} • {best.TableLabel} • {best.Probability:P2}";
            return new DeckCardRow(card.Id, card.Name, e.Copies, have, missing, farm, e.Role);
        }).ToArray();
    }

    private ImageSource LoadPortrait(FreeDuelistReference duelist)
    {
        if (_portraitCache.TryGetValue(duelist.Id, out var cached)) return cached;
        var path = Path.Combine(_portraitRoot, duelist.PortraitFile);
        if (!File.Exists(path)) throw new FileNotFoundException($"Bundled portrait for {duelist.Name} is missing.", path);
        using var stream = File.OpenRead(path);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return _portraitCache[duelist.Id] = bitmap;
    }

    private void SelectBuild_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CampaignDeckBlueprint build }) SelectBuild(build);
    }

    private void SelectBuild(CampaignDeckBlueprint build)
    {
        _analysisCancellation?.Cancel();
        _selected = build;
        BuildTitle.Text = build.Name + " • " + build.Stage;
        BuildDescription.Text = build.Description;
        BuildPlan.Text = build.Plan;
        BuildCautions.Text = build.Cautions;
        BuildRecipes.Text = string.Join(Environment.NewLine, build.Recipes.Select(r =>
            string.Join(" + ", r.Materials.Select(id => _catalog.GetCard(id).Name)) + " → " + _catalog.GetCard(r.ResultCardId).Name));
        BuildSources.ItemsSource = build.Sources;
        ReferenceMetrics.Text = _analyses.TryGetValue(build.Id, out var report) ? FormatMetrics(report) : "Check the reference deck to see its opening-hand setup availability.";
        AnalyzeReferenceButton.IsEnabled = _analysisCancellation is null;
        RefreshDetailInventory(_inventory().ToArray());
        BuildDetailScroll.ScrollToTop();
    }

    private async void AnalyzeReference_Click(object sender, RoutedEventArgs e)
    {
        if (_analysisCancellation is not null) return;
        var build = _selected;
        using var cancellation = new CancellationTokenSource();
        _analysisCancellation = cancellation;
        AnalyzeReferenceButton.IsEnabled = false;
        CancelReferenceButton.IsEnabled = true;
        ReferenceMetrics.Text = "Checking 658,008 physical opening hands…";
        try
        {
            var progress = new Progress<DeckAnalysisProgress>(p =>
            {
                if (!_closed && !cancellation.IsCancellationRequested && _selected.Id == build.Id)
                    ReferenceMetrics.Text = $"Checking {p.CompletedHands:N0} / {p.TotalHands:N0} opening hands…";
            });
            var report = await Task.Run(() => new DeckAnalyzer(_catalog, cacheByteLimit: 64L * 1024 * 1024,
                workerCount: DeckBuildJob.AutoAnalysisWorkerCount).Analyze(build.Expand(), false, progress, cancellation.Token));
            _analyses[build.Id] = report;
            if (!_closed && _selected.Id == build.Id) ReferenceMetrics.Text = FormatMetrics(report);
        }
        catch (OperationCanceledException)
        {
            if (!_closed && _selected.Id == build.Id) ReferenceMetrics.Text = "Check cancelled. You can check this deck again.";
        }
        catch (Exception error)
        {
            if (!_closed && _selected.Id == build.Id) ReferenceMetrics.Text = "Check failed: " + error.Message;
        }
        finally
        {
            _analysisCancellation = null;
            if (!_closed) { AnalyzeReferenceButton.IsEnabled = true; CancelReferenceButton.IsEnabled = false; }
        }
    }

    private static string FormatMetrics(DeckAnalysisReport r) =>
        $"Exact {r.TotalHands:N0} hands • 2,800+ body: {r.Body2800Probability:P1} • 3,500+ setup: {r.Setup3500Probability:P1} • >4,500 setup: {r.EndgamePowerProbability:P1}" +
        Environment.NewLine + $"Board clear in hand: {r.BoardClearProbability:P1} • >4,500 setup or clear: {r.EndgameAnswerProbability:P1} • No monster: {r.NoMonsterProbability:P1}";

    private void ReferenceSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchWatermark is null) return;
        var query = ReferenceSearchBox.Text.Trim();
        SearchWatermark.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (query.Length < 3)
        {
            SearchResults.ItemsSource = null;
            SearchPopup.IsOpen = false;
            return;
        }

        var duelists = _freeDuel.Duelists.Where(d => d.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(d => new SearchSuggestion(d.Name, "DUELIST • full reward tables", LoadPortrait(d), d, null));
        var cards = _catalog.Cards.Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(c => new SearchSuggestion(c.Name, $"CARD #{c.Id:D3} • {c.PrimaryType}", _artwork(c.Id), null, c));
        var matches = duelists.Concat(cards)
            .OrderByDescending(item => item.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
        SearchResults.ItemsSource = matches;
        SearchPopup.IsOpen = matches.Length > 0;
    }

    private void SearchSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SearchSuggestion suggestion }) return;
        SearchPopup.IsOpen = false;
        ReferenceSearchBox.Text = string.Empty;
        if (suggestion.Duelist is not null) ShowDuelist(suggestion.Duelist);
        else if (suggestion.Card is not null) ShowCard(suggestion.Card, null);
    }

    private void OpenFreeDuel_Click(object sender, RoutedEventArgs e) => LibraryTabs.SelectedItem = FreeDuelTab;

    private void DuelistTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FreeDuelistReference duelist }) ShowDuelist(duelist);
    }

    private void ShowDuelist(FreeDuelistReference duelist)
    {
        _lastDuelist = duelist;
        DuelistDetailPortrait.Source = LoadPortrait(duelist);
        DuelistDetailName.Text = $"{duelist.Id:D2} • {duelist.Name}";
        DuelistRewardTabs.Items.Clear();
        foreach (var table in duelist.RewardTables)
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                RowHeaderWidth = 0,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                ItemsSource = table.Drops.Select(drop => new DuelistDropRow(drop.CardId, _catalog.GetCard(drop.CardId).Name,
                    drop.Weight, drop.Probability.ToString("P2", CultureInfo.CurrentCulture))).ToArray()
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new System.Windows.Data.Binding(nameof(DuelistDropRow.CardId)) { StringFormat = "000" }, Width = 55 });
            grid.Columns.Add(new DataGridTextColumn { Header = "CARD", Binding = new System.Windows.Data.Binding(nameof(DuelistDropRow.Name)), Width = 330 });
            grid.Columns.Add(new DataGridTextColumn { Header = "WEIGHT / 2048", Binding = new System.Windows.Data.Binding(nameof(DuelistDropRow.Weight)), Width = 120 });
            grid.Columns.Add(new DataGridTextColumn { Header = "DROP", Binding = new System.Windows.Data.Binding(nameof(DuelistDropRow.Probability)), Width = 90 });
            grid.MouseDoubleClick += DuelistDropGrid_MouseDoubleClick;
            DuelistRewardTabs.Items.Add(new TabItem
            {
                Header = table.Label,
                Content = grid,
                Tag = table.Id,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            });
        }
        DuelistRewardTabs.SelectedIndex = 0;
        CardDetail.Visibility = Visibility.Collapsed;
        DuelistDetail.Visibility = Visibility.Visible;
        DetailOverlay.Visibility = Visibility.Visible;
    }

    private void DuelistDropGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: DuelistDropRow row }) ShowCard(_catalog.GetCard(row.CardId), _lastDuelist);
    }

    private void BuildCards_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BuildCards.SelectedItem is DeckCardRow row) ShowCard(_catalog.GetCard(row.CardId), null);
    }

    private void ShowCard(Card card, FreeDuelistReference? backToDuelist)
    {
        CardDetailArtwork.Source = _artwork(card.Id);
        CardDetailName.Text = $"{card.Id:D3} • {card.Name}";
        var level = card.Level is null ? string.Empty : $" • Level {card.Level}";
        var attribute = string.IsNullOrWhiteSpace(card.Attribute) ? string.Empty : $" • {card.Attribute}";
        CardDetailStats.Text = $"{card.PrimaryType}{attribute}{level} • ATK {card.Attack:N0} / DEF {card.Defense:N0}";
        CardDetailStars.Text = FormatGuardianStars(card);
        CardDetailDescription.Text = string.IsNullOrWhiteSpace(card.Description) ? "No description is stored for this card." : card.Description;
        CardDetailShop.Text = !string.IsNullOrWhiteSpace(card.Password) && card.StarchipCost is > 0
            ? $"PASSWORD SHOP • {card.Password} • {card.StarchipCost.Value:N0} Star Chips"
            : "PASSWORD SHOP • unavailable";
        var rows = _freeDuel.DropsForCard(card.Id).Select(drop => new CardDropRow(
            _freeDuel.Duelists.Single(d => d.Id == drop.DuelistId), drop.DuelistName, drop.TableLabel,
            drop.Probability.ToString("P2", CultureInfo.CurrentCulture))).ToArray();
        CardDropSources.ItemsSource = rows;
        CardDropSources.Visibility = rows.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        NoDropSources.Text = rows.Length == 0 ? "This card is not available from an original Free Duel reward table." : _freeDuel.ProbabilityNote;
        BackToDuelistButton.Visibility = backToDuelist is null ? Visibility.Collapsed : Visibility.Visible;
        _lastDuelist = backToDuelist;
        DuelistDetail.Visibility = Visibility.Collapsed;
        CardDetail.Visibility = Visibility.Visible;
        DetailOverlay.Visibility = Visibility.Visible;
    }

    private static string FormatGuardianStars(Card card)
    {
        static string Display(string? star) => string.IsNullOrWhiteSpace(star) ? "—"
            : GuardianStarRules.TryGetSymbol(star, out var symbol) ? $"{symbol} {star}" : star;
        return $"Guardian Stars • {Display(card.GuardianStar1)}  /  {Display(card.GuardianStar2)}";
    }

    private void DropDuelist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FreeDuelistReference duelist }) ShowDuelist(duelist);
    }

    private void BackToDuelist_Click(object sender, RoutedEventArgs e)
    {
        if (_lastDuelist is not null) ShowDuelist(_lastDuelist);
    }

    private void CloseOverlay_Click(object sender, RoutedEventArgs e) => CloseOverlay();
    private void CloseOverlay()
    {
        DetailOverlay.Visibility = Visibility.Collapsed;
        CardDetail.Visibility = Visibility.Collapsed;
        DuelistDetail.Visibility = Visibility.Collapsed;
    }

    private void LibraryTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UseBuildButton is null || AdaptHint is null) return;
        var deckTab = LibraryTabs.SelectedItem == RecommendedDecksTab;
        UseBuildButton.Visibility = deckTab ? Visibility.Visible : Visibility.Collapsed;
        AdaptHint.Visibility = deckTab ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CancelReference_Click(object sender, RoutedEventArgs e) => _analysisCancellation?.Cancel();

    private void BuildCards_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (BuildCards.Columns.Count < 6) return;
        var flexible = Math.Max(0, e.NewSize.Width - 390);
        BuildCards.Columns[0].Width = Math.Max(145, flexible * .34);
        BuildCards.Columns[4].Width = Math.Max(190, flexible * .39);
        BuildCards.Columns[5].Width = Math.Max(125, flexible * .27);
    }

    private void UseBuild_Click(object sender, RoutedEventArgs e)
    {
        if (!_canUseBuild()) { RefreshInventory(); return; }
        _useBuild(_selected);
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Source_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri.Scheme != Uri.UriSchemeHttps) return;
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) { ReferenceMetrics.Text = "Could not open source: " + error.Message; }
        e.Handled = true;
    }

    private sealed record DeckTile(CampaignDeckBlueprint Build, ImageSource? Artwork, Brush Accent, string OwnedLine);
    private sealed record DeckCardRow(int CardId, string Name, int Required, long Owned, long Missing, string BestFarm, string Role);
    private sealed record DuelistTile(FreeDuelistReference Reference, ImageSource Portrait);
    private sealed record SearchSuggestion(string Name, string KindLabel, ImageSource? Image, FreeDuelistReference? Duelist, Card? Card);
    private sealed record DuelistDropRow(int CardId, string Name, int Weight, string Probability);
    private sealed record CardDropRow(FreeDuelistReference Duelist, string DuelistName, string Rank, string Probability);
}
