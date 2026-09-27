using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    private readonly Dictionary<string, DeckAnalysisReport> _analyses = [];
    private CampaignDeckBlueprint _selected;
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
        InitializeComponent();
        RefreshInventory();
        SelectBuild(_selected);
        Closed += (_, _) => { _closed = true; _analysisCancellation?.Cancel(); _queuedRefresh?.Abort(); };
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
        BuildCards.ItemsSource = _selected.Entries.Select(e => new DeckCardRow(_catalog.GetCard(e.CardId).Name,
            e.Copies, quantities.GetValueOrDefault(e.CardId), Math.Max(0, e.Copies - quantities.GetValueOrDefault(e.CardId)), e.Role)).ToArray();
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

    private void CancelReference_Click(object sender, RoutedEventArgs e) => _analysisCancellation?.Cancel();
    private void BuildCards_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var flexible = Math.Max(0, e.NewSize.Width - 206);
        BuildCards.Columns[0].Width = flexible * .58;
        BuildCards.Columns[4].Width = flexible * .42;
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
    private sealed record DeckCardRow(string Name, int Required, long Owned, long Missing, string Role);
}
