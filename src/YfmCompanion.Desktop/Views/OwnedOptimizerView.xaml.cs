using System.Windows;
using System.Windows.Controls;

namespace YfmCompanion.Desktop.Views;

/// <summary>Tab-specific presentation; commands are forwarded to the shared application workflow.</summary>
public partial class OwnedOptimizerView : UserControl
{
    public OwnedOptimizerView() => InitializeComponent();
    public event EventHandler<RoutedEventArgs>? RefreshSaveSnapshot_ClickRequested;
    private void RefreshSaveSnapshot_Click(object sender, RoutedEventArgs e) => RefreshSaveSnapshot_ClickRequested?.Invoke(sender, e);
    public event EventHandler<RoutedEventArgs>? SelectSave_ClickRequested;
    private void SelectSave_Click(object sender, RoutedEventArgs e) => SelectSave_ClickRequested?.Invoke(sender, e);
    public event EventHandler<RoutedEventArgs>? UseAutomaticSave_ClickRequested;
    private void UseAutomaticSave_Click(object sender, RoutedEventArgs e) => UseAutomaticSave_ClickRequested?.Invoke(sender, e);
    public event EventHandler<RoutedEventArgs>? UseManualCollection_ClickRequested;
    private void UseManualCollection_Click(object sender, RoutedEventArgs e) => UseManualCollection_ClickRequested?.Invoke(sender, e);
    public event EventHandler<SelectionChangedEventArgs>? CampaignScope_SelectionChangedRequested;
    private void CampaignScope_SelectionChanged(object sender, SelectionChangedEventArgs e) => CampaignScope_SelectionChangedRequested?.Invoke(sender, e);
    public event EventHandler<RoutedEventArgs>? CampaignStarChipSetting_ChangedRequested;
    private void CampaignStarChipSetting_Changed(object sender, RoutedEventArgs e) => CampaignStarChipSetting_ChangedRequested?.Invoke(sender, e);
    public event EventHandler<SelectionChangedEventArgs>? OptimizerProfile_SelectionChangedRequested;
    private void OptimizerProfile_SelectionChanged(object sender, SelectionChangedEventArgs e) => OptimizerProfile_SelectionChangedRequested?.Invoke(sender, e);
    public event EventHandler<RoutedEventArgs>? OptimizeDeck_ClickRequested;
    private void OptimizeDeck_Click(object sender, RoutedEventArgs e) => OptimizeDeck_ClickRequested?.Invoke(sender, e);
    public event EventHandler<RoutedEventArgs>? PauseOptimization_ClickRequested;
    private void PauseOptimization_Click(object sender, RoutedEventArgs e) => PauseOptimization_ClickRequested?.Invoke(sender, e);
    public event EventHandler<RoutedEventArgs>? StopOptimization_ClickRequested;
    private void StopOptimization_Click(object sender, RoutedEventArgs e) => StopOptimization_ClickRequested?.Invoke(sender, e);
    public event EventHandler<RoutedEventArgs>? VerifyOptimization_ClickRequested;
    private void VerifyOptimization_Click(object sender, RoutedEventArgs e) => VerifyOptimization_ClickRequested?.Invoke(sender, e);
    public event EventHandler<RoutedEventArgs>? ClearOwned_ClickRequested;
    private void ClearOwned_Click(object sender, RoutedEventArgs e) => ClearOwned_ClickRequested?.Invoke(sender, e);
    public event EventHandler<SizeChangedEventArgs>? OptimizerBody_SizeChangedRequested;
    private void OptimizerBody_SizeChanged(object sender, SizeChangedEventArgs e) => OptimizerBody_SizeChangedRequested?.Invoke(sender, e);
    public event EventHandler<TextChangedEventArgs>? OwnedSearch_TextChangedRequested;
    private void OwnedSearch_TextChanged(object sender, TextChangedEventArgs e) => OwnedSearch_TextChangedRequested?.Invoke(sender, e);
    public event EventHandler<SelectionChangedEventArgs>? OwnedFilter_ChangedRequested;
    private void OwnedFilter_Changed(object sender, SelectionChangedEventArgs e) => OwnedFilter_ChangedRequested?.Invoke(sender, e);
    public event EventHandler<RoutedEventArgs>? AddOwnedCard_ClickRequested;
    private void AddOwnedCard_Click(object sender, RoutedEventArgs e) => AddOwnedCard_ClickRequested?.Invoke(sender, e);
    public event EventHandler<RoutedEventArgs>? ChooseArtworkFolder_ClickRequested;
    private void ChooseArtworkFolder_Click(object sender, RoutedEventArgs e) => ChooseArtworkFolder_ClickRequested?.Invoke(sender, e);
    public event EventHandler<RoutedEventArgs>? AssignSelectedArtwork_ClickRequested;
    private void AssignSelectedArtwork_Click(object sender, RoutedEventArgs e) => AssignSelectedArtwork_ClickRequested?.Invoke(sender, e);
    public event EventHandler<SelectionChangedEventArgs>? OwnedCards_SelectionChangedRequested;
    private void OwnedCards_SelectionChanged(object sender, SelectionChangedEventArgs e) => OwnedCards_SelectionChangedRequested?.Invoke(sender, e);
}

