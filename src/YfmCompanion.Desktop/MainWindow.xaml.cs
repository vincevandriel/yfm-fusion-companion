using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using YfmCompanion.Data;
using YfmCompanion.Desktop.Controls;
using YfmCompanion.Engine;
using YfmCompanion.RetroArch;

namespace YfmCompanion.Desktop;

public partial class MainWindow : Window
{
    private readonly List<CardPicker> _turnPickers = [];
    private readonly List<CardPicker> _handPickers = [];
    private readonly List<CardPicker> _monsterPickers = [];
    private readonly List<CardPicker> _spellPickers = [];
    private readonly List<CardPicker> _deckPickers = [];
    private readonly List<OwnedCardRow> _ownedCardRows = [];
    private readonly DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CancellationTokenSource _windowCancellation = new();
    private readonly LocalDiagnosticLog _diagnostics = new();
    private IReadOnlyList<OwnedCardRow> _visibleOwnedCardRows = [];
    private FusionCatalog? _catalog;
    private TacticalFusionPlanner? _planner;
    private DeckAnalyzer? _deckAnalyzer;
    private OwnedDeckOptimizer? _ownedDeckOptimizer;
    private ForbiddenMemoriesStrategyEvaluator? _strategyEvaluator;
    private CampaignResearchData? _campaignResearchData;
    private CampaignDeckOptimizer? _campaignDeckOptimizer;
    private CancellationTokenSource? _deckAnalysisCancellation;
    private CancellationTokenSource? _optimizationCancellation;
    private SaveSnapshot? _saveSnapshot;
    private RetroArchNetworkClient? _liveClient;
    private ForbiddenMemoriesLiveReader? _liveReader;
    private bool _liveReadInProgress;
    private int _livePort;
    private Rect _normalModeBounds;
    private string? _lastSavePath;
    private string _lastBadge = string.Empty;
    private string _lastLoggedLiveBadge = string.Empty;
    private string _liveBadge = "DISCONNECTED";
    private string _liveBadgeColor = "#7B3B45";
    private bool _compactMode;
    private bool _inspectorOpen;
    private bool _synchronizingTopmost;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        _liveTimer.Tick += LiveTimer_Tick;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RestoreDesktopSettings();
        var databasePath = Path.Combine(AppContext.BaseDirectory, "Data", "yfm.db");
        if (!File.Exists(databasePath))
        {
            MessageBox.Show($"The bundled card database was not found:\n{databasePath}", "YFM Fusion Companion", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        try
        {
            _catalog = FusionCatalog.Load(databasePath);
            _planner = new TacticalFusionPlanner(_catalog);
            _deckAnalyzer = new DeckAnalyzer(_catalog);
            _ownedDeckOptimizer = new OwnedDeckOptimizer(_catalog);
            _strategyEvaluator = new ForbiddenMemoriesStrategyEvaluator(_catalog);
            var search = new CardSearchService(_catalog.Cards);
            AddPickers(HandPickerPanel, _handPickers, "Hand", search);
            AddPickers(MonsterPickerPanel, _monsterPickers, "Monster", search);
            AddPickers(SpellPickerPanel, _spellPickers, "Spell / Trap", search);
            AddDeckPickers(search);
            InitializeOptimizer(_catalog);
            InitializeCampaignOptimizer(_catalog);
            DatabaseStatus.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"Offline database ready • {_catalog.Cards.Count:N0} cards • {_catalog.FusionPairs.Count:N0} resolved fusion pairs");
            _diagnostics.Add("Application", "Database ready", "Bundled offline card database validated.");
            _handPickers[0].FocusInput();
            if (!await TryLoadRememberedSaveAsync())
            {
                await RefreshSaveSnapshotAsync(silentWhenNone: true);
            }
            await RefreshLiveAsync();
            _liveTimer.Start();
        }
        catch (Exception exception)
        {
            _diagnostics.Add("Application", "Startup failed", exception.Message);
            MessageBox.Show($"The card database could not be loaded.\n\n{exception.Message}", "YFM Fusion Companion", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _deckAnalysisCancellation?.Cancel();
        _optimizationCancellation?.Cancel();
        _windowCancellation.Cancel();
        SaveDesktopSettings();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _liveTimer.Stop();
        _liveClient?.Dispose();
        _windowCancellation.Dispose();
    }

    private void RestoreDesktopSettings()
    {
        var settings = DesktopSettingsStore.Load();
        _lastSavePath = settings.LastSavePath;
        AlwaysOnTopCheckBox.IsChecked = settings.AlwaysOnTop;
        CompactTopmostCheckBox.IsChecked = settings.AlwaysOnTop;
        Topmost = settings.AlwaysOnTop;

        var minimumSavedWidth = settings.CompactMode ? 160 : 620;
        var minimumSavedHeight = settings.CompactMode ? 160 : 420;
        if (settings.Width >= minimumSavedWidth && settings.Width <= 10000 &&
            settings.Height >= minimumSavedHeight && settings.Height <= 10000 &&
            settings.Left is double left && settings.Top is double top)
        {
            var savedBounds = new Rect(left, top, settings.Width.Value, settings.Height.Value);
            var virtualScreen = new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
            if (savedBounds.IntersectsWith(virtualScreen))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = savedBounds.Left;
                Top = savedBounds.Top;
                Width = savedBounds.Width;
                Height = savedBounds.Height;
            }
        }

        ApplyCompactMode(settings.CompactMode, resizeWindow: false);
        if (settings.IsMaximized && !settings.CompactMode)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SaveDesktopSettings()
    {
        try
        {
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            DesktopSettingsStore.Save(new DesktopSettings(
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                WindowState == WindowState.Maximized,
                Topmost,
                _compactMode,
                _lastSavePath));
        }
        catch (Exception exception)
        {
            _diagnostics.Add("Preferences", "Save failed", exception.Message);
        }
    }

    private async Task<bool> TryLoadRememberedSaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_lastSavePath) || !File.Exists(_lastSavePath))
        {
            return false;
        }

        try
        {
            var inspection = await Task.Run(() => Ps1MemoryCardReader.Inspect(_lastSavePath));
            if (inspection.Snapshot is null)
            {
                _diagnostics.Add("Saved snapshot", "Remembered path unavailable", inspection.Error ?? "Validation failed.");
                return false;
            }

            ShowSaveSnapshot(inspection.Snapshot);
            return true;
        }
        catch (Exception exception)
        {
            _diagnostics.Add("Saved snapshot", "Remembered path failed", exception.Message);
            return false;
        }
    }

    private async void LiveTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshLiveAsync();
    }

    private async Task RefreshLiveAsync()
    {
        if (_liveReadInProgress || _catalog is null)
        {
            return;
        }

        _liveReadInProgress = true;
        try
        {
            if (!RetroArchConfigInspector.IsRetroArchRunning())
            {
                SetLiveUnavailable("RetroArch is not running.", "Start RetroArch and Forbidden Memories. The companion will retry automatically every 5 seconds.", "DISCONNECTED", "#7B3B45");
                return;
            }

            var configPath = RetroArchConfigInspector.FindConfigurationPath();
            var config = RetroArchConfigInspector.Read(configPath ?? string.Empty);
            if (!config.Exists || !config.NetworkCommandsEnabled)
            {
                SetLiveUnavailable(
                    "RetroArch found — memory access is not enabled.",
                    "In RetroArch open Settings → Network → Network Commands and turn it ON. The companion retries automatically and will not edit RetroArch settings. Keep Windows Firewall blocking inbound UDP 55355 from other computers.",
                    "DISCONNECTED",
                    "#7B3B45");
                return;
            }

            if (_liveClient is null || _livePort != config.NetworkCommandPort)
            {
                _liveClient?.Dispose();
                _livePort = config.NetworkCommandPort;
                _liveClient = new RetroArchNetworkClient(port: _livePort);
                _liveReader = new ForbiddenMemoriesLiveReader(_liveClient);
            }

            var snapshot = await _liveReader!.ReadSnapshotAsync(_windowCancellation.Token);
            ShowLiveSnapshot(snapshot);
        }
        catch (OperationCanceledException) when (_windowCancellation.IsCancellationRequested)
        {
            // Window is closing.
        }
        catch (TimeoutException)
        {
            SetLiveUnavailable(
                "RetroArch is running, but its read-only UDP interface did not answer.",
                "If you just enabled Network Commands, restart RetroArch once. The companion will retry automatically every 5 seconds.",
                "DISCONNECTED",
                "#7B3B45");
        }
        catch (RetroArchNoContentException exception)
        {
            SetLiveUnavailable("RetroArch connected — no game is running.", exception.Message, "NO CONTENT", "#805C1D", updateSucceeded: true);
        }
        catch (RetroArchWrongContentException exception)
        {
            SetLiveUnavailable("RetroArch connected — different game detected.", exception.Message, "WRONG GAME", "#9A3F4D", updateSucceeded: true);
        }
        catch (RetroArchTransientStateException exception)
        {
            SetLiveUnavailable(
                "The duel changed during this refresh.",
                $"{exception.Message} The companion will retry automatically in 5 seconds.",
                "UPDATING",
                "#805C1D",
                updateSucceeded: true);
        }
        catch (RetroArchProtocolException exception)
        {
            SetLiveUnavailable("Live memory is not ready.", exception.Message, "UNAVAILABLE", "#7B3B45");
        }
        catch (Exception exception)
        {
            SetLiveUnavailable("The live connection stopped safely.", exception.Message, "DISCONNECTED", "#7B3B45");
        }
        finally
        {
            _liveReadInProgress = false;
        }
    }

    private void SetLiveUnavailable(
        string headline,
        string detail,
        string badge,
        string badgeColor,
        bool updateSucceeded = false)
    {
        SetLiveHealth(updateSucceeded);
        SetLiveStateBadge(badge, badgeColor, detail);
        LiveConnectionText.Text = headline;
        LiveStatusText.Text = detail;
        LivePlayerLpText.Text = "—";
        LiveOpponentLpText.Text = "—";
        LiveTerrainText.Text = "—";
        LiveAdviceSummary.Text = "Live fusion advice will appear after a validated duel snapshot.";
        LiveHandGrid.ItemsSource = null;
        LivePlayerFieldGrid.ItemsSource = null;
        LivePlayerSpellTrapGrid.ItemsSource = null;
        LiveOpponentFieldGrid.ItemsSource = null;
        LiveDeckGrid.ItemsSource = null;
        LiveCollectionGrid.ItemsSource = null;
        LiveAdviceGrid.ItemsSource = null;
        CompactAdviceGrid.ItemsSource = null;
    }

    private void SetLiveHealth(bool isHealthy)
    {
        var healthText = isHealthy ? "UP TO DATE" : "ERROR";
        var healthBrush = (Brush)FindResource(isHealthy ? "SuccessBrush" : "ErrorBrush");
        LiveUpdateHealthText.Text = healthText;
        LiveUpdateHealthText.Foreground = healthBrush;
    }

    private void ShowLiveSnapshot(ForbiddenMemoriesLiveSnapshot snapshot)
    {
        if (_catalog is null || _planner is null)
        {
            return;
        }

        SetLiveHealth(isHealthy: true);
        SetLiveStateBadge(
            snapshot.DuelActive ? "LIVE" : "NOT IN DUEL",
            snapshot.DuelActive ? "#176B52" : "#805C1D",
            "Validated read-only RetroArch snapshot.");
        var gameActivity = snapshot.DuelActive
            ? "duel active"
            : snapshot.SaveDataAvailable
                ? "game active, outside duel"
                : "title/startup, save not loaded";
        LiveConnectionText.Text = $"{snapshot.Status.GameBasename} • {gameActivity} • {snapshot.MemoryCommand}";
        LiveStatusText.Text = RetroArchConfigInspector.IsUdpListenerExposedBeyondLoopback(_livePort)
            ? $"Connected locally. CAUTION: RetroArch is listening beyond loopback on UDP {_livePort}; keep Windows Firewall blocking inbound access from other computers."
            : "Connected locally. No input, cheats, writes, or game-process scanning are used.";
        LivePlayerLpText.Text = snapshot.DuelActive
            ? snapshot.PlayerLifePoints.ToString("N0", CultureInfo.InvariantCulture)
            : "—";
        LiveOpponentLpText.Text = snapshot.DuelActive
            ? snapshot.OpponentLifePoints.ToString("N0", CultureInfo.InvariantCulture)
            : "—";
        LiveTerrainText.Text = snapshot.DuelActive ? TerrainName(snapshot.TerrainId) : "—";

        if (!_compactMode)
        {
            LiveDeckGrid.ItemsSource = snapshot.ConstructedDeckCardIds
                .Select((cardId, index) => new SaveDeckRow(index + 1, _catalog.GetCard(cardId)))
                .ToArray();
            var deckQuantities = snapshot.ConstructedDeckCardIds
                .GroupBy(cardId => cardId)
                .ToDictionary(group => group.Key, group => group.Count());
            LiveCollectionGrid.ItemsSource = snapshot.SaveDataAvailable ? _catalog.Cards
                .OrderBy(card => card.Id)
                .Select(card =>
                {
                    var chestQuantity = snapshot.ChestQuantities[card.Id - 1];
                    var deckQuantity = deckQuantities.GetValueOrDefault(card.Id);
                    return new SaveCollectionRow(card, chestQuantity, deckQuantity, chestQuantity + deckQuantity, false);
                })
                .Where(row => row.Total > 0)
                .ToArray() : [];
        }

        var liveHandRows = snapshot.HandCardIds
            .Select((cardId, index) => (cardId, index))
            .Where(item => item.cardId is >= 1 and <= 722)
            .Select(item => ToLiveCardRow(item.index + 1, item.cardId, null))
            .ToArray();
        LiveHandGrid.ItemsSource = liveHandRows;
        if (!_compactMode)
        {
            LivePlayerFieldGrid.ItemsSource = snapshot.PlayerField
                .Select(fieldCard => ToLiveCardRow(fieldCard.Slot, fieldCard.CardId, fieldCard, snapshot.TerrainId))
                .ToArray();
            LivePlayerSpellTrapGrid.ItemsSource = snapshot.PlayerSpellTrapField
                .Select(fieldCard => ToLiveCardRow(fieldCard.Slot, fieldCard.CardId, fieldCard, snapshot.TerrainId))
                .ToArray();
            LiveOpponentFieldGrid.ItemsSource = snapshot.OpponentField
                .Select(fieldCard => ToLiveCardRow(fieldCard.Slot, fieldCard.CardId, fieldCard, snapshot.TerrainId))
                .ToArray();
        }

        if (!snapshot.DuelActive)
        {
            LiveAdviceGrid.ItemsSource = null;
            LiveAdviceSummary.Text = snapshot.SaveDataAvailable
                ? "Constructed deck and owned collection are live. Hand, field, life points, and turn advice will activate automatically when a duel begins."
                : "Connected to Forbidden Memories. Load a saved game to make the constructed deck and owned collection available; duel advice will activate automatically when a duel begins.";
            CompactAdviceGrid.ItemsSource = null;
            return;
        }

        var hand = snapshot.HandCardIds
            .Select((cardId, index) => new HandCard(index + 1, cardId))
            .Where(card => card.CardId is >= 1 and <= 722)
            .ToArray();
        var monsters = snapshot.PlayerField
            .Select(fieldCard => new FieldCard(FieldZone.Monster, fieldCard.Slot, fieldCard.CardId));
        var spells = snapshot.PlayerSpellTrapField
            .Select(fieldCard => new FieldCard(FieldZone.SpellTrap, fieldCard.Slot, fieldCard.CardId));
        var tacticalRecommendations = _planner.FindRecommendations(hand, monsters, spells, true)
            .Take(100)
            .ToArray();
        var recommendations = tacticalRecommendations
            .Select(ToResultRow)
            .ToArray();
        LiveAdviceGrid.ItemsSource = recommendations;
        CompactAdviceGrid.ItemsSource = tacticalRecommendations
            .Take(20)
            .Select(CompactLivePresentation.CreateRow)
            .ToArray();
        LiveAdviceSummary.Text = recommendations.Length == 0
            ? "No valid fusion or final equip route is available from the current hand and active field."
            : $"{recommendations.Length:N0} best legal routes from the current hand order; updated automatically. Equips are applied only to the final monster.";
    }

    private LiveCardRow ToLiveCardRow(int slot, int cardId, LiveFieldCard? liveField, int terrainId = 0)
    {
        var card = _catalog!.GetCard(cardId);
        var terrainModifier = liveField is null || terrainId is < 1 or > 6
            ? 0
            : ForbiddenMemoriesStrategyEvaluator.GetFieldModifier(329 + terrainId, card.PrimaryType);
        return new LiveCardRow(
            slot,
            card.Id,
            card.Name,
            card.PrimaryType,
            Math.Max(0, (liveField?.Attack ?? card.Attack) + (liveField?.PowerModifier ?? 0) + terrainModifier),
            Math.Max(0, (liveField?.Defense ?? card.Defense) + (liveField?.PowerModifier ?? 0) + terrainModifier));
    }

    private static bool IsSpellOrTrap(Card card) => card.PrimaryType is
        "Magic" or "Spell" or "Trap" or "Equip" or "Ritual";

    private static string TerrainName(int terrainId) => terrainId switch
    {
        1 => "Forest",
        2 => "Wasteland",
        3 => "Mountain",
        4 => "Sogen",
        5 => "Umi",
        6 => "Yami",
        _ => "Normal"
    };

    private void InitializeOptimizer(FusionCatalog catalog)
    {
        var profiles = new[]
        {
            new ProfileChoice(DeckStrategyProfile.Balanced, "Balanced", "Strong-fusion probabilities first, while retaining useful control, equips, and flexible secondary routes."),
            new ProfileChoice(DeckStrategyProfile.FusionConsistency, "Fusion consistency", "Favors material overlap and independent routes so weak five-card draws can still chain."),
            new ProfileChoice(DeckStrategyProfile.MaximumPower, "Maximum power", "Favors the highest reachable results and stronger standalone monsters over route breadth."),
            new ProfileChoice(DeckStrategyProfile.ControlAndSafety, "Control and safety", "Raises Raigeki, broad traps, stall, debuffs, and matchup-relevant removal."),
            new ProfileChoice(DeckStrategyProfile.FieldAndType, "Field and type", "Builds around the selected field and monster types, including its +500 and -500 matchups."),
            new ProfileChoice(DeckStrategyProfile.RitualExperiment, "Ritual experiment", "Allows ritual packages for deliberate testing; rituals remain disfavored in normal profiles.")
        };
        OptimizerProfileCombo.ItemsSource = profiles;
        OptimizerProfileCombo.SelectedIndex = 0;
        PreferredFieldCombo.ItemsSource = new[]
        {
            new FieldChoice(null, "Automatic / none"),
            new FieldChoice(330, "Forest"),
            new FieldChoice(331, "Wasteland"),
            new FieldChoice(332, "Mountain"),
            new FieldChoice(333, "Sogen"),
            new FieldChoice(334, "Umi"),
            new FieldChoice(335, "Yami")
        };
        PreferredFieldCombo.SelectedIndex = 0;

        _ownedCardRows.AddRange(catalog.Cards.Select(card => new OwnedCardRow(card)));
        _visibleOwnedCardRows = _ownedCardRows;
        OwnedCardsGrid.ItemsSource = _visibleOwnedCardRows;
    }

    private void InitializeCampaignOptimizer(FusionCatalog catalog)
    {
        try
        {
            _campaignResearchData = CampaignResearchData.LoadBundled(AppContext.BaseDirectory);
            _campaignDeckOptimizer = new CampaignDeckOptimizer(catalog, _campaignResearchData);
            CampaignScopeCombo.ItemsSource = new[]
            {
                new CampaignScopeChoice(OptimizerGoal.GeneralCampaign, "General campaign • recommended", "Builds a general-purpose deck for the 33 normal campaign opponents. Near-equal choices favor final-gauntlet viability."),
                new CampaignScopeChoice(OptimizerGoal.SpecificOpponent, "One specific opponent", "Builds around the concrete card pool and reachable threats of the selected duelist."),
                new CampaignScopeChoice(OptimizerGoal.FinalGauntlet, "Final boss gauntlet", "Builds a special configuration for the six end-game gauntlet opponents."),
                new CampaignScopeChoice(OptimizerGoal.ManualCustom, "Manual custom profile", "Uses the profile, type, field, and opponent-type inputs without campaign research targeting.")
            };
            RefreshCampaignOpponentChoices();
            CampaignScopeCombo.SelectedIndex = 0;
            UpdateCampaignControls();
            _diagnostics.Add("Campaign optimizer", "Research loaded", "Validated bundled opponent and policy data is available to the desktop optimizer.");
        }
        catch (Exception exception)
        {
            _campaignResearchData = null;
            _campaignDeckOptimizer = null;
            CampaignScopeCombo.IsEnabled = false;
            CampaignOpponentCombo.IsEnabled = false;
            UseSavedStarChipsCheckBox.IsEnabled = false;
            CampaignInputHint.Text = "Campaign research data could not be loaded. Manual custom optimization remains available.";
            _diagnostics.Add("Campaign optimizer", "Research unavailable", exception.Message);
        }
    }

    private void RefreshCampaignOpponentChoices()
    {
        if (_campaignResearchData is null)
        {
            return;
        }

        var selectedId = CampaignOpponentCombo.SelectedValue is int duelistId ? duelistId : (int?)null;
        CampaignOpponentCombo.ItemsSource = _campaignResearchData.Opponents.Values
            .OrderBy(opponent => opponent.DuelistId)
            .Select(opponent => new OpponentChoice(
                opponent.DuelistId,
                _saveSnapshot is null
                    ? $"{opponent.DuelistId:00} • {opponent.Name}"
                    : _saveSnapshot.UnlockedDuelistIds.Contains(opponent.DuelistId)
                        ? $"{opponent.DuelistId:00} • {opponent.Name} • unlocked"
                        : $"{opponent.DuelistId:00} • {opponent.Name} • not unlocked in this save"))
            .ToArray();
        CampaignOpponentCombo.SelectedValue = selectedId ?? 1;
    }

    private void CampaignScope_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateCampaignControls();

    private void CampaignStarChipSetting_Changed(object sender, RoutedEventArgs e) =>
        UpdateCampaignControls();

    private void UpdateCampaignControls()
    {
        if (CampaignScopeCombo is null || CampaignInputHint is null)
        {
            return;
        }

        var goal = CampaignScopeCombo.SelectedValue is OptimizerGoal selectedGoal
            ? selectedGoal
            : OptimizerGoal.GeneralCampaign;
        var isCampaignGoal = goal != OptimizerGoal.ManualCustom && _campaignDeckOptimizer is not null;
        var hasSavedBudget = _saveSnapshot is not null;
        var choosingSpecificOpponent = goal == OptimizerGoal.SpecificOpponent && isCampaignGoal;
        CampaignOpponentCombo.Visibility = choosingSpecificOpponent ? Visibility.Visible : Visibility.Collapsed;
        CampaignOpponentCombo.IsEnabled = choosingSpecificOpponent;
        CampaignOpponentHint.Visibility = choosingSpecificOpponent ? Visibility.Collapsed : Visibility.Visible;
        UseSavedStarChipsCheckBox.IsEnabled = isCampaignGoal && hasSavedBudget;
        if (!UseSavedStarChipsCheckBox.IsEnabled)
        {
            UseSavedStarChipsCheckBox.IsChecked = false;
        }

        if (isCampaignGoal)
        {
            OptimizerProfileCombo.SelectedValue = DeckStrategyProfile.ControlAndSafety;
        }

        CampaignInputHint.Text = goal switch
        {
            OptimizerGoal.SpecificOpponent => "Choose the duelist you want to beat. Threat ordering is useful matchup guidance, not a promised win rate.",
            OptimizerGoal.FinalGauntlet => "This is a separate late-game deck configuration, not the general campaign recommendation.",
            OptimizerGoal.ManualCustom => "Manual custom mode uses the existing profile, type, field, and opponent-type inputs. Saved Star Chips are used only by a campaign plan.",
            _ when hasSavedBudget => string.Create(CultureInfo.InvariantCulture, $"General campaign plan ready. Saved snapshot budget: {_saveSnapshot!.StarChips:N0} Star Chips. Checking the box creates a virtual purchase plan only."),
            _ => "Load a saved snapshot to use its Star Chip budget; the optimizer otherwise uses only the owned quantities below."
        };
    }

    private void AddDeckPickers(CardSearchService search)
    {
        for (var slot = 1; slot <= 40; slot++)
        {
            var picker = new CardPicker { Width = 196, Margin = new Thickness(0, 0, 10, 8) };
            picker.Configure($"Deck {slot}", search);
            picker.CardChanged += Picker_CardChanged;
            picker.AdvanceRequested += Picker_AdvanceRequested;
            DeckPickerPanel.Children.Add(picker);
            _deckPickers.Add(picker);
        }
    }

    private void AddPickers(System.Windows.Controls.Panel panel, List<CardPicker> group, string label, CardSearchService search)
    {
        for (var slot = 1; slot <= 5; slot++)
        {
            var picker = new CardPicker();
            picker.Configure($"{label} {slot}", search);
            picker.CardChanged += Picker_CardChanged;
            picker.AdvanceRequested += Picker_AdvanceRequested;
            panel.Children.Add(picker);
            group.Add(picker);
            _turnPickers.Add(picker);
        }
    }

    private void Picker_CardChanged(object? sender, Card? card)
    {
        if (card is null || _catalog is null)
        {
            return;
        }

        ShowInspector(card);
    }

    private void ShowInspector(Card card)
    {
        if (_catalog is null)
        {
            return;
        }

        var details = _catalog.GetAdvancedDetails(card.Id);
        InspectorPrompt.Visibility = Visibility.Collapsed;
        InspectorContent.Visibility = Visibility.Visible;
        InspectorName.Text = card.Name;
        InspectorId.Text = string.Create(CultureInfo.InvariantCulture, $"CARD #{card.Id:000}");
        InspectorType.Text = card.PrimaryType;
        InspectorAttack.Text = card.Attack.ToString("N0", CultureInfo.InvariantCulture);
        InspectorDefense.Text = card.Defense.ToString("N0", CultureInfo.InvariantCulture);
        InspectorAdvanced.Text = BuildAdvancedText(details);
    }

    private void LiveCard_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_catalog is null || sender is not DataGrid grid || grid.SelectedItem is null)
        {
            return;
        }

        var card = grid.SelectedItem switch
        {
            LiveCardRow row => _catalog.GetCard(row.CardId),
            SaveDeckRow row => row.Card,
            SaveCollectionRow row => row.Card,
            TurnResultRow row => _catalog.GetCard(row.Result),
            _ => null
        };

        if (card is null)
        {
            return;
        }

        ShowInspector(card);
        OpenInspector();
    }

    private void ToggleInspector_Click(object sender, RoutedEventArgs e)
    {
        if (_inspectorOpen)
        {
            CloseInspector();
        }
        else
        {
            OpenInspector();
        }
    }

    private void CloseInspector_Click(object sender, RoutedEventArgs e) => CloseInspector();

    private void OpenInspector()
    {
        if (_compactMode || WorkspaceTabs.SelectedItem != LiveDuelTab)
        {
            return;
        }

        _inspectorOpen = true;
        InspectorPanel.Visibility = Visibility.Visible;
        InspectorToggleButton.Content = "CLOSE INSPECTOR";
        InspectorTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
        {
            From = Math.Max(InspectorPanel.ActualWidth, InspectorPanel.Width),
            To = 0,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void CloseInspector(bool immediate = false)
    {
        _inspectorOpen = false;
        InspectorToggleButton.Content = "INSPECT CARDS";
        if (InspectorPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        var closedPosition = Math.Max(InspectorPanel.ActualWidth, InspectorPanel.Width);
        if (immediate)
        {
            InspectorTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            InspectorTranslate.X = closedPosition;
            InspectorPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var animation = new DoubleAnimation
        {
            From = InspectorTranslate.X,
            To = closedPosition,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        animation.Completed += (_, _) =>
        {
            if (!_inspectorOpen)
            {
                InspectorPanel.Visibility = Visibility.Collapsed;
            }
        };
        InspectorTranslate.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private void Picker_AdvanceRequested(object? sender, EventArgs e)
    {
        if (sender is not CardPicker picker)
        {
            return;
        }

        var index = _turnPickers.IndexOf(picker);
        if (index >= 0 && index + 1 < _turnPickers.Count)
        {
            _turnPickers[index + 1].FocusInput();
            return;
        }

        index = _deckPickers.IndexOf(picker);
        if (index >= 0 && index + 1 < _deckPickers.Count)
        {
            _deckPickers[index + 1].FocusInput();
        }
    }

    private void AnalyzeTurn_Click(object sender, RoutedEventArgs e)
    {
        if (_planner is null)
        {
            return;
        }

        SetStateBadge("MANUAL", "#394B59", "Manual turn analysis selected.");
        var hand = SelectedCards(_handPickers).Select(item => new HandCard(item.Slot, item.Card.Id));
        var monsters = SelectedCards(_monsterPickers).Select(item => new FieldCard(FieldZone.Monster, item.Slot, item.Card.Id));
        var spells = SelectedCards(_spellPickers).Select(item => new FieldCard(FieldZone.SpellTrap, item.Slot, item.Card.Id));
        var recommendations = _planner.FindRecommendations(hand, monsters, spells, IncludeGlitchesCheckBox.IsChecked == true);
        var rows = recommendations.Select(ToResultRow).ToArray();
        TurnResultsGrid.ItemsSource = rows;
        TurnResultSummary.Text = rows.Length == 0
            ? "No valid fusion or equip route was found for the supplied cards."
            : string.Create(CultureInfo.InvariantCulture, $"{rows.Length:N0} legal routes, ranked by final ATK then DEF. Field interaction is terminal.");
    }

    private void ClearTurn_Click(object sender, RoutedEventArgs e)
    {
        foreach (var picker in _turnPickers)
        {
            picker.Clear();
        }

        TurnResultsGrid.ItemsSource = null;
        TurnResultSummary.Text = "Enter any available cards; empty slots are ignored.";
        InspectorContent.Visibility = Visibility.Collapsed;
        InspectorPrompt.Visibility = Visibility.Visible;
        _handPickers.FirstOrDefault()?.FocusInput();
    }

    private async void AnalyzeDeck_Click(object sender, RoutedEventArgs e)
    {
        if (_deckAnalyzer is null || _deckAnalysisCancellation is not null)
        {
            return;
        }

        SetStateBadge("MANUAL", "#394B59", "Manual deck analysis selected.");
        var analyzer = _deckAnalyzer;
        var deck = _deckPickers
            .Where(picker => picker.SelectedCard is not null)
            .Select(picker => picker.SelectedCard!.Id)
            .ToArray();
        var includeGlitches = DeckIncludeGlitchesCheckBox.IsChecked == true;
        _deckAnalysisCancellation = new CancellationTokenSource();
        var cancellationToken = _deckAnalysisCancellation.Token;
        AnalyzeDeckButton.IsEnabled = false;
        CancelDeckButton.IsEnabled = true;
        DeckResultsGrid.ItemsSource = null;
        ResetDeckMetrics();

        var progress = new Progress<DeckAnalysisProgress>(value =>
        {
            DeckResultSummary.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"Examining hand {value.CompletedHands:N0} of {value.TotalHands:N0} • {value.Fraction:P1}");
        });

        try
        {
            var report = await Task.Run(
                () => analyzer.Analyze(deck, includeGlitches, progress, cancellationToken),
                cancellationToken);
            ShowDeckReport(report);
        }
        catch (OperationCanceledException)
        {
            DeckResultSummary.Text = "Deck analysis cancelled.";
        }
        catch (Exception exception)
        {
            DeckResultSummary.Text = $"Deck analysis failed: {exception.Message}";
        }
        finally
        {
            _deckAnalysisCancellation.Dispose();
            _deckAnalysisCancellation = null;
            AnalyzeDeckButton.IsEnabled = true;
            CancelDeckButton.IsEnabled = false;
        }
    }

    private void CancelDeck_Click(object sender, RoutedEventArgs e) =>
        _deckAnalysisCancellation?.Cancel();

    private void ClearDeck_Click(object sender, RoutedEventArgs e)
    {
        _deckAnalysisCancellation?.Cancel();
        foreach (var picker in _deckPickers)
        {
            picker.Clear();
        }

        DeckResultsGrid.ItemsSource = null;
        ResetDeckMetrics();
        DeckResultSummary.Text = "Fill any number of slots. A 40-card deck examines exactly 658,008 physical five-card hands.";
        _deckPickers.FirstOrDefault()?.FocusInput();
    }

    private async void LoadCurrentDeckFromSave_Click(object sender, RoutedEventArgs e)
    {
        if (_catalog is null)
        {
            return;
        }

        LoadCurrentDeckButton.IsEnabled = false;
        DeckResultSummary.Text = "Reading the current 40-card deck from the saved memory card…";
        try
        {
            var snapshot = await ReadCurrentSaveSnapshotAsync();
            if (snapshot is null)
            {
                DeckResultSummary.Text = "No valid Forbidden Memories save was found. Open SAVE SNAPSHOT to refresh or choose the .srm/.mcr file manually.";
                return;
            }

            ShowSaveSnapshot(snapshot);
            LoadDeckAnalyzerFromSnapshot(snapshot);
            DeckResultSummary.Text = $"Loaded the current 40-card saved deck from {Path.GetFileName(snapshot.FilePath)}. Ready for exact hand analysis.";
            _diagnostics.Add("Deck analyzer", "Current saved deck loaded", "Re-read and validated the saved memory-card file before filling all 40 deck slots.");
        }
        catch (Exception exception)
        {
            DeckResultSummary.Text = $"The current saved deck could not be loaded: {exception.Message}";
            _diagnostics.Add("Deck analyzer", "Current saved deck load failed", exception.Message);
        }
        finally
        {
            LoadCurrentDeckButton.IsEnabled = true;
        }
    }

    private async Task<SaveSnapshot?> ReadCurrentSaveSnapshotAsync()
    {
        if (!string.IsNullOrWhiteSpace(_lastSavePath) && File.Exists(_lastSavePath))
        {
            var rememberedInspection = await Task.Run(() => Ps1MemoryCardReader.Inspect(_lastSavePath));
            if (rememberedInspection.Snapshot is not null)
            {
                return rememberedInspection.Snapshot;
            }
        }

        var discovery = await Task.Run(() => RetroArchSaveLocator.Discover());
        return discovery.SelectedSnapshot;
    }

    private async void RefreshSaveSnapshot_Click(object sender, RoutedEventArgs e) =>
        await RefreshSaveSnapshotAsync(silentWhenNone: false);

    private async Task RefreshSaveSnapshotAsync(bool silentWhenNone)
    {
        RefreshSaveButton.IsEnabled = false;
        SaveSnapshotStatus.Text = "Refreshing from RetroArch's configured SwanStation memory-card folder…";
        try
        {
            var discovery = await Task.Run(() => RetroArchSaveLocator.Discover());
            if (discovery.SelectedSnapshot is not null)
            {
                ShowSaveSnapshot(discovery.SelectedSnapshot);
                return;
            }

            var failureDetails = discovery.Inspections.Count == 0
                ? "No Forbidden Memories .srm or .mcr files were found in the configured RetroArch save folder."
                : string.Join(" • ", discovery.Inspections.Select(result => $"{Path.GetFileName(result.FilePath)}: {result.Error}"));
            SaveSnapshotStatus.Text = silentWhenNone
                ? "No valid saved snapshot was found in RetroArch's save folder; manual entry and file selection remain available."
                : failureDetails;
            _diagnostics.Add("Saved snapshot", "Refresh unavailable", failureDetails);
        }
        catch (Exception exception)
        {
            SaveSnapshotStatus.Text = $"Saved-snapshot refresh failed without affecting manual mode: {exception.Message}";
            _diagnostics.Add("Saved snapshot", "Refresh failed", exception.Message);
        }
        finally
        {
            RefreshSaveButton.IsEnabled = true;
        }
    }

    private async void SelectSave_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a Forbidden Memories memory-card image",
            Filter = "PlayStation memory cards (*.srm;*.mcr)|*.srm;*.mcr|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (!string.IsNullOrWhiteSpace(_lastSavePath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_lastSavePath);
            dialog.FileName = Path.GetFileName(_lastSavePath);
        }
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        SaveSnapshotStatus.Text = "Reading the selected memory-card file…";
        var inspection = await Task.Run(() => Ps1MemoryCardReader.Inspect(dialog.FileName));
        if (inspection.Snapshot is null)
        {
            SaveSnapshotStatus.Text = $"The selected file was not imported: {inspection.Error}";
            _diagnostics.Add("Saved snapshot", "Import rejected", inspection.Error ?? "Validation failed.");
            return;
        }

        ShowSaveSnapshot(inspection.Snapshot);
    }

    private void ShowSaveSnapshot(SaveSnapshot snapshot)
    {
        if (_catalog is null)
        {
            return;
        }

        _saveSnapshot = snapshot;
        _lastSavePath = snapshot.FilePath;
        RefreshCampaignOpponentChoices();
        UpdateCampaignControls();
        SetStateBadge("SAVED SNAPSHOT", "#365A86", "Validated read-only saved snapshot selected.");
        _diagnostics.Add("Saved snapshot", "Loaded", "Remembered validated local save path.");
        SaveSourceText.Text = $"Saved snapshot (not live) • {snapshot.SourceFormat} • bank {snapshot.MemoryCardBank}, block {snapshot.BlockNumber}";
        SavePathText.Text = snapshot.FilePath;
        var localTime = snapshot.LastWriteTimeUtc.ToLocalTime();
        SaveTimestampText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{localTime:yyyy-MM-dd HH:mm:ss zzz} local • {snapshot.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss} UTC");
        var age = DateTime.UtcNow - snapshot.LastWriteTimeUtc;
        var ageText = age >= TimeSpan.Zero
            ? string.Create(CultureInfo.InvariantCulture, $"Snapshot age: {age.TotalDays:N1} days.")
            : "The file timestamp is in the future relative to this computer.";
        var warnings = snapshot.Warnings.Count == 0
            ? string.Empty
            : $" Warnings: {string.Join(" ", snapshot.Warnings)}";
        SaveValidationText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Directory checksum valid • file identity {snapshot.DirectoryFileName} • both 0x680-byte save copies match • 40 valid card IDs • {snapshot.StarChips:N0} Star Chips • {snapshot.UnlockedDuelistIds.Count:N0}/{Ps1MemoryCardReader.DuelistCount:N0} Free Duel opponents unlocked. {ageText} RetroArch may not flush a new save until the game closes, and the in-game Library must be opened before saving for its flags to refresh.{warnings}");
        SaveSnapshotStatus.Text = $"Loaded {Path.GetFileName(snapshot.FilePath)} as a saved snapshot. No game or save data was modified.";
        ApplyOwnedButton.IsEnabled = true;
        ApplyDeckButton.IsEnabled = true;
        SaveDeckGrid.ItemsSource = snapshot.DeckCardIds
            .Select((cardId, index) => new SaveDeckRow(index + 1, _catalog.GetCard(cardId)))
            .ToArray();
        SaveCollectionGrid.ItemsSource = _catalog.Cards
            .OrderBy(card => card.Id)
            .Select(card => new SaveCollectionRow(
                card,
                snapshot.GetChestQuantity(card.Id),
                snapshot.GetDeckQuantity(card.Id),
                snapshot.GetTotalOwned(card.Id),
                snapshot.LibraryCardIds.Contains(card.Id)))
            .Where(row => row.Total > 0 || row.Seen)
            .ToArray();
    }

    private void ApplyOwnedSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_saveSnapshot is null)
        {
            return;
        }

        foreach (var row in _ownedCardRows)
        {
            row.Quantity = _saveSnapshot.GetTotalOwned(row.Card.Id);
        }

        OwnedCardsGrid.Items.Refresh();
        ResetOptimizerResults();
        OptimizationStatus.Text = $"Loaded owned quantities from saved snapshot {Path.GetFileName(_saveSnapshot.FilePath)} (chest + constructed deck).";
        WorkspaceTabs.SelectedItem = OwnedOptimizerTab;
    }

    private void ApplyDeckSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_saveSnapshot is null || _catalog is null)
        {
            return;
        }

        LoadDeckAnalyzerFromSnapshot(_saveSnapshot);
        DeckResultSummary.Text = $"Loaded 40 cards from saved snapshot {Path.GetFileName(_saveSnapshot.FilePath)}. Ready for exact hand analysis.";
        WorkspaceTabs.SelectedItem = DeckAnalyzerTab;
    }

    private void LoadDeckAnalyzerFromSnapshot(SaveSnapshot snapshot)
    {
        _deckAnalysisCancellation?.Cancel();
        for (var index = 0; index < _deckPickers.Count; index++)
        {
            _deckPickers[index].SetCard(_catalog!.GetCard(snapshot.DeckCardIds[index]));
        }

        DeckResultsGrid.ItemsSource = null;
        ResetDeckMetrics();
    }

    private async void OptimizeDeck_Click(object sender, RoutedEventArgs e)
    {
        if (_ownedDeckOptimizer is null || _optimizationCancellation is not null)
        {
            return;
        }

        OwnedCardsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        OwnedCardsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var owned = _ownedCardRows
            .Where(row => row.Quantity > 0)
            .Select(row => new OwnedCardQuantity(row.Card.Id, row.Quantity))
            .ToArray();
        var goal = CampaignScopeCombo.SelectedValue is OptimizerGoal selectedGoal
            ? selectedGoal
            : OptimizerGoal.ManualCustom;
        var isCampaignGoal = goal != OptimizerGoal.ManualCustom;
        if (isCampaignGoal && _campaignDeckOptimizer is null)
        {
            OptimizationStatus.Text = "Campaign research data is unavailable, so this goal cannot be calculated. Choose Manual custom profile or restart with the bundled research data present.";
            return;
        }

        var useSavedStarChips = isCampaignGoal && UseSavedStarChipsCheckBox.IsChecked == true;
        var starChips = useSavedStarChips ? _saveSnapshot?.StarChips ?? 0U : 0U;
        var legalOwnedCapacity = owned.Sum(item => Math.Min(item.Quantity, OwnedDeckOptimizer.LegalCopyLimitForCard(item.CardId)));
        if (legalOwnedCapacity < 40 && !useSavedStarChips)
        {
            OptimizationStatus.Text = "At least 40 usable owned copies are required after the three-copy limit and one-copy Exodia-piece limit.";
            return;
        }

        var profile = isCampaignGoal
            ? DeckStrategyProfile.ControlAndSafety
            : OptimizerProfileCombo.SelectedValue is DeckStrategyProfile selectedProfile
            ? selectedProfile
            : DeckStrategyProfile.Balanced;
        var preferredFieldId = PreferredFieldCombo.SelectedValue is int fieldId ? fieldId : (int?)null;
        var options = new DeckOptimizationOptions(
            IncludeGlitches: OptimizerIncludeGlitchesCheckBox.IsChecked == true,
            Profile: profile,
            PreferredMonsterTypes: ParseTypes(PreferredTypesTextBox.Text),
            PreferredFieldCardId: preferredFieldId,
            OpponentMonsterTypes: ParseTypes(OpponentTypesTextBox.Text));
        var currentDeck = _deckPickers
            .Where(picker => picker.SelectedCard is not null)
            .Select(picker => picker.SelectedCard!.Id)
            .ToArray();
        var comparisonDeck = currentDeck.Length == 40 ? currentDeck : null;
        var specificOpponentId = CampaignOpponentCombo.SelectedValue is int selectedOpponentId
            ? selectedOpponentId
            : (int?)null;
        if (goal == OptimizerGoal.SpecificOpponent && specificOpponentId is null)
        {
            OptimizationStatus.Text = "Choose a target duelist before building an opponent-specific deck.";
            return;
        }

        SetStateBadge(
            isCampaignGoal ? "CAMPAIGN PLAN" : "MANUAL",
            isCampaignGoal ? "#365A86" : "#394B59",
            isCampaignGoal ? "Campaign-owned-card optimization selected." : "Manual owned-card optimization selected.");
        var optimizer = _ownedDeckOptimizer;
        var campaignOptimizer = _campaignDeckOptimizer;
        _optimizationCancellation = new CancellationTokenSource();
        var cancellationToken = _optimizationCancellation.Token;
        OptimizeDeckButton.IsEnabled = false;
        CancelOptimizationButton.IsEnabled = true;
        ResetOptimizerResults();
        var progress = new Progress<DeckOptimizationProgress>(value =>
        {
            OptimizationStatus.Text = value.Total == 0
                ? value.Stage
                : string.Create(CultureInfo.InvariantCulture, $"{value.Stage} • {value.Completed:N0}/{value.Total:N0} • {value.Fraction:P0}");
        });

        try
        {
            if (isCampaignGoal)
            {
                var campaignPlan = await Task.Run(
                    () => campaignOptimizer!.Optimize(
                        owned,
                        starChips,
                        useSavedStarChips,
                        ToCampaignOpponentScope(goal),
                        specificOpponentId,
                        options,
                        comparisonDeck,
                        progress,
                        cancellationToken),
                    cancellationToken);
                ShowOptimizationReport(campaignPlan.DeckPlan.ResultingDeck);
                ShowCampaignPlan(campaignPlan);
            }
            else
            {
                var report = await Task.Run(
                    () => optimizer!.Optimize(owned, options, comparisonDeck, progress, cancellationToken),
                    cancellationToken);
                ShowOptimizationReport(report);
            }
        }
        catch (OperationCanceledException)
        {
            OptimizationStatus.Text = "Deck optimization cancelled.";
        }
        catch (Exception exception)
        {
            OptimizationStatus.Text = $"Deck optimization failed: {exception.Message}";
        }
        finally
        {
            _optimizationCancellation.Dispose();
            _optimizationCancellation = null;
            OptimizeDeckButton.IsEnabled = true;
            CancelOptimizationButton.IsEnabled = false;
        }
    }

    private void CancelOptimization_Click(object sender, RoutedEventArgs e) =>
        _optimizationCancellation?.Cancel();

    private void ClearOwned_Click(object sender, RoutedEventArgs e)
    {
        _optimizationCancellation?.Cancel();
        foreach (var row in _ownedCardRows)
        {
            row.Quantity = 0;
        }

        OwnedCardsGrid.Items.Refresh();
        ResetOptimizerResults();
        OptimizationStatus.Text = "Owned quantities cleared.";
    }

    private void SetVisibleOwned_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _visibleOwnedCardRows)
        {
            row.Quantity = 3;
        }

        OwnedCardsGrid.Items.Refresh();
        OptimizationStatus.Text = string.Create(CultureInfo.InvariantCulture, $"Set {_visibleOwnedCardRows.Count:N0} visible cards to three owned copies.");
    }

    private void OwnedSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (OwnedCardsGrid is null)
        {
            return;
        }

        var query = OwnedSearchBox.Text.Trim();
        _visibleOwnedCardRows = string.IsNullOrEmpty(query)
            ? _ownedCardRows
            : _ownedCardRows
                .Where(row => row.Card.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                              row.Card.Id.ToString(CultureInfo.InvariantCulture).StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        OwnedCardsGrid.ItemsSource = _visibleOwnedCardRows;
    }

    private void OwnedCards_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OwnedCardsGrid.SelectedItem is OwnedCardRow row)
        {
            ShowInspector(row.Card);
        }
    }

    private void OptimizerProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OptimizerProfileDescription is not null && OptimizerProfileCombo.SelectedItem is ProfileChoice profile)
        {
            OptimizerProfileDescription.Text = profile.Description;
        }
    }

    private void ShowOptimizationReport(DeckOptimizationReport report)
    {
        Optimizer2800Metric.Text = FormatProbability(report.ExactAnalysis.AtLeast2800Probability);
        Optimizer2500Metric.Text = FormatProbability(report.ExactAnalysis.AtLeast2500Probability);
        OptimizerExpectedMetric.Text = report.ExactAnalysis.ExpectedBestFusionAttack.ToString("N0", CultureInfo.InvariantCulture);
        OptimizerDeadMetric.Text = FormatProbability(report.ExactAnalysis.DeadHandProbability);
        OptimizedDeckGrid.ItemsSource = report.Deck;
        OptimizationTargetsGrid.ItemsSource = report.ImportantTargets.Select(target => new OptimizationTargetRow(
            target.Result,
            target.EffectiveAttack,
            FormatProbability(target.Probability),
            target.RepresentativeRoute)).ToArray();
        LimitedCardsGrid.ItemsSource = report.LimitedCards;
        ExcludedCardsGrid.ItemsSource = report.ExcludedOrLowValueCards;
        var comparison = report.Comparison is null
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture,
                $" • vs entered 40-card deck: ≥2800 {report.Comparison.AtLeast2800ProbabilityChange:+0.00%;-0.00%;0.00%}, expected ATK {report.Comparison.ExpectedBestAttackChange:+0;-0;0}");
        OptimizationStatus.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Complete • exact analysis of {report.ExactAnalysis.TotalHands:N0} hands • {report.TotalCards} cards • seed {report.RandomSeed}{comparison}");
    }

    private void ShowCampaignPlan(CampaignDeckPlan campaignPlan)
    {
        var report = campaignPlan.DeckPlan.ResultingDeck;
        CampaignPlanPanel.Visibility = Visibility.Visible;
        CampaignScopeSummary.Text = $"{campaignPlan.Context.Safety.Label} • Best found; not a proof of global optimality.";
        var safety = report.SafetyAssessment is null
            ? "No campaign safety assessment was available."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Counter coverage score: {report.SafetyAssessment.HeuristicScore:N0} across {report.SafetyAssessment.ThreatCount:N0} concrete threats. This is a heuristic, not a duel-win probability.");
        var finalGauntlet = report.SecondarySafetyAssessment is null
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture,
                $" Final-gauntlet tie-break score: {report.SecondarySafetyAssessment.HeuristicScore:N0}.");
        CampaignSafetySummary.Text = safety + finalGauntlet;
        CampaignPurchaseSummary.Text = campaignPlan.DeckPlan.StarChipsEnabled
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Virtual purchase plan: spend {campaignPlan.DeckPlan.SpentStarChips:N0} of {campaignPlan.DeckPlan.StartingStarChips:N0} saved Star Chips; {campaignPlan.DeckPlan.RemainingStarChips:N0} remain. Nothing was written to the save.")
            : "No Star Chips used. This deck was built only from the owned quantities entered or loaded above.";
        PurchasePlanGrid.ItemsSource = campaignPlan.DeckPlan.Purchases;
        CampaignThreatsGrid.ItemsSource = campaignPlan.Context.Safety.Threats
            .OrderByDescending(threat => threat.Attack)
            .ThenByDescending(threat => threat.Importance)
            .ThenBy(threat => threat.OpponentName, StringComparer.OrdinalIgnoreCase)
            .Select(threat => new CampaignThreatRow(
                threat.OpponentName,
                threat.ThreatCardName,
                threat.Attack,
                FormatGuardianStars(threat.PossibleGuardianStars),
                threat.IsFusionThreat ? "Reachable fusion" : "Strongest base monster"))
            .ToArray();
        OptimizationStatus.Text += " • campaign plan complete; threat ordering and counter scores are guidance, not a guaranteed win rate.";
    }

    private void ResetOptimizerResults()
    {
        Optimizer2800Metric.Text = "—";
        Optimizer2500Metric.Text = "—";
        OptimizerExpectedMetric.Text = "—";
        OptimizerDeadMetric.Text = "—";
        OptimizedDeckGrid.ItemsSource = null;
        OptimizationTargetsGrid.ItemsSource = null;
        LimitedCardsGrid.ItemsSource = null;
        ExcludedCardsGrid.ItemsSource = null;
        PurchasePlanGrid.ItemsSource = null;
        CampaignThreatsGrid.ItemsSource = null;
        CampaignPlanPanel.Visibility = Visibility.Collapsed;
        CampaignScopeSummary.Text = string.Empty;
        CampaignSafetySummary.Text = string.Empty;
        CampaignPurchaseSummary.Text = string.Empty;
    }

    private static CampaignOpponentScope ToCampaignOpponentScope(OptimizerGoal goal) => goal switch
    {
        OptimizerGoal.GeneralCampaign => CampaignOpponentScope.GeneralSafety,
        OptimizerGoal.SpecificOpponent => CampaignOpponentScope.SpecificOpponent,
        OptimizerGoal.FinalGauntlet => CampaignOpponentScope.FinalGauntlet,
        _ => throw new ArgumentOutOfRangeException(nameof(goal), "Manual custom mode has no campaign opponent scope.")
    };

    private static string FormatGuardianStars(IReadOnlyList<string> stars) =>
        stars.Count == 0
            ? "?"
            : string.Join(" / ", stars.Select(star =>
                GuardianStarRules.TryGetSymbol(star, out var symbol) ? symbol : "?"));

    private static string[] ParseTypes(string text) =>
        [.. text.Split([',', ';', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private void ShowDeckReport(DeckAnalysisReport report)
    {
        AnyFusionMetric.Text = FormatProbability(report.AnyFusionProbability);
        AtLeast2500Metric.Text = FormatProbability(report.AtLeast2500Probability);
        AtLeast2800Metric.Text = FormatProbability(report.AtLeast2800Probability);
        AtLeast3000Metric.Text = FormatProbability(report.AtLeast3000Probability);
        ExpectedAttackMetric.Text = report.ExpectedBestFusionAttack.ToString("N0", CultureInfo.InvariantCulture);
        DeadHandMetric.Text = FormatProbability(report.DeadHandProbability);
        DeckResultsGrid.ItemsSource = report.FusionResults.Select(result => new DeckResultRow(
            result.IsEquipped ? $"{result.Result.Name} (equipped)" : result.Result.Name,
            result.EffectiveAttack,
            result.EffectiveDefense,
            FormatProbability(result.Probability),
            string.Create(CultureInfo.InvariantCulture, $"{result.HandsContainingResult:N0} / {result.TotalHands:N0}"),
            FormatDeckRoute(result.RepresentativeRoute),
            result.RepresentativeRoute.ContainsGlitch)).ToArray();
        DeckResultSummary.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Complete • {report.TotalHands:N0} distinct physical {report.HandSize}-card hands from {report.DeckSize} entered cards • results ranked by ATK then DEF");
    }

    private void ResetDeckMetrics()
    {
        AnyFusionMetric.Text = "—";
        AtLeast2500Metric.Text = "—";
        AtLeast2800Metric.Text = "—";
        AtLeast3000Metric.Text = "—";
        ExpectedAttackMetric.Text = "—";
        DeadHandMetric.Text = "—";
    }

    private static string FormatDeckRoute(DeckFusionRoute route)
    {
        if (route.Materials.Count < 2)
        {
            return route.Materials.Count == 0 ? "—" : route.Materials[0].Name;
        }

        var text = $"{route.Materials[0].Name} + {route.Materials[1].Name} = {route.IntermediateResults[0].Name}";
        for (var index = 2; index < route.Materials.Count; index++)
        {
            text += $" + {route.Materials[index].Name} = {route.IntermediateResults[index - 1].Name}";
        }

        return route.EndsWithEquip ? $"{text} [FINAL EQUIP +{route.EquipBonus:N0} ATK/DEF]" : text;
    }

    private static string FormatProbability(double probability) =>
        probability.ToString("P2", CultureInfo.InvariantCulture);

    private void AlwaysOnTop_Changed(object sender, RoutedEventArgs e)
    {
        if (_synchronizingTopmost || sender is not System.Windows.Controls.CheckBox checkBox)
        {
            return;
        }

        _synchronizingTopmost = true;
        try
        {
            var isTopmost = checkBox.IsChecked == true;
            Topmost = isTopmost;
            AlwaysOnTopCheckBox.IsChecked = isTopmost;
            CompactTopmostCheckBox.IsChecked = isTopmost;
        }
        finally
        {
            _synchronizingTopmost = false;
        }
    }

    private void CompactMode_Click(object sender, RoutedEventArgs e) =>
        ApplyCompactMode(!_compactMode, resizeWindow: true);

    private void ApplyCompactMode(bool compact, bool resizeWindow)
    {
        if (compact == _compactMode && FullWorkspace.Visibility == (compact ? Visibility.Collapsed : Visibility.Visible))
        {
            return;
        }

        if (compact && resizeWindow)
        {
            _normalModeBounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;
            WindowState = WindowState.Normal;
        }

        if (compact)
        {
            CloseInspector(immediate: true);
        }

        _compactMode = compact;
        FullWorkspace.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactWorkspace.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        AppHeader.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        DatabaseStatus.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        RootLayout.Margin = compact ? new Thickness(5) : new Thickness(20);
        MinWidth = compact ? 0 : 1080;
        MinHeight = compact ? 0 : 700;
        if (compact)
        {
            SetStateBadge(_liveBadge, _liveBadgeColor, "Compact live adviser selected.");
        }
        else
        {
            UpdateBadgeForSelectedWorkspace();
        }

        if (!resizeWindow || WindowState != WindowState.Normal)
        {
            return;
        }

        if (compact)
        {
            const double compactWidth = 320;
            Width = Math.Min(compactWidth, SystemParameters.WorkArea.Width);
            Height = SystemParameters.WorkArea.Height;
            Left = SystemParameters.WorkArea.Right - Width;
            Top = SystemParameters.WorkArea.Top;
        }
        else if (!_normalModeBounds.IsEmpty)
        {
            Left = _normalModeBounds.Left;
            Top = _normalModeBounds.Top;
            Width = Math.Max(MinWidth, _normalModeBounds.Width);
            Height = Math.Max(MinHeight, _normalModeBounds.Height);
        }
        else
        {
            Width = Math.Min(1420, SystemParameters.WorkArea.Width);
            Height = Math.Min(900, SystemParameters.WorkArea.Height);
        }
    }

    private void SetStateBadge(string label, string color, string diagnosticDetail)
    {
        SourceModeText.Text = label;
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        SourceBadgeBorder.Background = brush;
        if (!string.Equals(_lastBadge, label, StringComparison.Ordinal))
        {
            _lastBadge = label;
            _diagnostics.Add("Live state", label, diagnosticDetail);
        }
    }

    private void SetLiveStateBadge(string label, string color, string diagnosticDetail)
    {
        _liveBadge = label;
        _liveBadgeColor = color;
        if (_compactMode || WorkspaceTabs.SelectedItem == LiveDuelTab)
        {
            SetStateBadge(label, color, diagnosticDetail);
        }
        else if (!string.Equals(_lastLoggedLiveBadge, label, StringComparison.Ordinal))
        {
            _lastLoggedLiveBadge = label;
            _diagnostics.Add("Live state", label, diagnosticDetail);
        }
    }

    private void WorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != WorkspaceTabs)
        {
            return;
        }

        if (WorkspaceTabs.SelectedItem != LiveDuelTab)
        {
            CloseInspector(immediate: true);
        }

        if (IsLoaded && !_compactMode)
        {
            UpdateBadgeForSelectedWorkspace();
        }
    }

    private void UpdateBadgeForSelectedWorkspace()
    {
        if (WorkspaceTabs.SelectedItem == LiveDuelTab)
        {
            SetStateBadge(_liveBadge, _liveBadgeColor, "Live workspace selected.");
        }
        else if (WorkspaceTabs.SelectedItem == SaveSyncTab)
        {
            SetStateBadge("SAVED SNAPSHOT", "#365A86", "Saved-snapshot workspace selected.");
        }
        else
        {
            SetStateBadge("MANUAL", "#394B59", "Manual analysis workspace selected.");
        }
    }

    private void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export YFM Fusion Companion diagnostics",
            Filter = "Text files (*.txt)|*.txt",
            DefaultExt = ".txt",
            AddExtension = true,
            FileName = $"YFM-Fusion-Companion-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, _diagnostics.ExportText(), Encoding.UTF8);
            DatabaseStatus.Text = $"Diagnostics exported to {dialog.FileName}";
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Diagnostics could not be exported.\n\n{exception.Message}", "YFM Fusion Companion", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static IEnumerable<(int Slot, Card Card)> SelectedCards(IReadOnlyList<CardPicker> pickers) =>
        pickers
            .Select((picker, index) => (Slot: index + 1, picker.SelectedCard))
            .Where(item => item.SelectedCard is not null)
            .Select(item => (item.Slot, item.SelectedCard!));

    private static TurnResultRow ToResultRow(TacticalRecommendation recommendation)
    {
        var route = string.Join(" → ", recommendation.Steps.Select(FormatStep));
        var field = recommendation.FieldTarget is null
            ? "—"
            : $"{recommendation.FieldTarget.Zone} {recommendation.FieldTarget.Slot}";
        return new TurnResultRow(
            recommendation.FinalCard.Name,
            recommendation.EffectiveAttack,
            recommendation.EffectiveDefense,
            string.Join(" + ", recommendation.ConsumedHandSlots),
            route,
            field,
            recommendation.ContainsGlitch);
    }

    private static string FormatStep(TacticalStep step) => step.Kind switch
    {
        TacticalStepKind.StartFromHand => $"H{step.SourceSlot} {step.Material.Name}",
        TacticalStepKind.StartFromField => $"{ZoneName(step)} {step.Material.Name}",
        TacticalStepKind.FuseFromHand => $"+ H{step.SourceSlot} {step.Material.Name} = {step.Result.Name}",
        TacticalStepKind.EquipFromHand => $"+ H{step.SourceSlot} {step.Material.Name} = {step.Result.Name} (+{step.AttackBonus:N0})",
        TacticalStepKind.FuseOntoField => $"+ {ZoneName(step)} {step.Material.Name} = {step.Result.Name}",
        TacticalStepKind.EquipOntoField => $"+ {ZoneName(step)} {step.Material.Name} = {step.Result.Name} (+{step.AttackBonus:N0})",
        _ => step.Result.Name
    };

    private static string ZoneName(TacticalStep step) =>
        step.SourceZone == FieldZone.Monster ? $"M{step.SourceSlot}" : $"S{step.SourceSlot}";

    private static string BuildAdvancedText(CardAdvancedDetails details)
    {
        var card = details.Card;
        var categories = details.Categories.Count == 0 ? "—" : string.Join(", ", details.Categories);
        return $"Level: {card.Level?.ToString(CultureInfo.InvariantCulture) ?? "—"}\n" +
               $"Attribute: {card.Attribute ?? "—"}\n" +
               $"Guardian stars: {card.GuardianStar1 ?? "—"} / {card.GuardianStar2 ?? "—"}\n" +
               $"Categories: {categories}\n" +
               $"Password: {card.Password ?? "—"}\n" +
               $"Starchip cost: {card.StarchipCost?.ToString("N0", CultureInfo.InvariantCulture) ?? "—"}\n" +
               $"Droppable: {YesNo(card.Droppable)}\n" +
               $"Fusible: {YesNo(card.Fusible)}\n" +
               $"Starter available: {YesNo(card.StarterAvailable)}\n" +
               $"Fusion partners: {details.FusionPartnerCount:N0}\n" +
               $"Recipes producing this card: {details.FusionRecipeCount:N0}\n" +
               $"Compatible equip targets: {details.CanEquipCount:N0}\n" +
               $"Compatible equips for this card: {details.EquippedByCount:N0}\n\n" +
               $"{card.Description ?? "No description available."}";
    }

    private static string YesNo(bool value) => value ? "Yes" : "No";

    private sealed record TurnResultRow(
        string Result,
        int Attack,
        int Defense,
        string HandOrder,
        string Route,
        string Field,
        bool IsGlitch);

    private sealed record DeckResultRow(
        string Result,
        int Attack,
        int Defense,
        string Probability,
        string Hands,
        string Route,
        bool IsGlitch);

    private sealed class OwnedCardRow(Card card)
    {
        public Card Card { get; } = card;

        public int Quantity { get; set; }
    }

    private sealed record ProfileChoice(DeckStrategyProfile Profile, string Label, string Description);

    private sealed record FieldChoice(int? CardId, string Label);

    private enum OptimizerGoal
    {
        GeneralCampaign,
        SpecificOpponent,
        FinalGauntlet,
        ManualCustom
    }

    private sealed record CampaignScopeChoice(OptimizerGoal Scope, string Label, string Description);

    private sealed record OpponentChoice(int DuelistId, string Label);

    private sealed record OptimizationTargetRow(
        Card Result,
        int EffectiveAttack,
        string FormattedProbability,
        string Route);

    private sealed record CampaignThreatRow(
        string Opponent,
        string Threat,
        int Attack,
        string GuardianStars,
        string Source);

    private sealed record SaveDeckRow(int Slot, Card Card);

    private sealed record SaveCollectionRow(Card Card, int Chest, int Deck, int Total, bool Seen);

    private sealed record LiveCardRow(
        int Slot,
        int CardId,
        string Name,
        string Type,
        int Attack,
        int Defense);
}
