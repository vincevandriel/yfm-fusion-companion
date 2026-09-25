using System.Windows;
using System.Windows.Controls;

namespace YfmCompanion.Desktop.Views;

/// <summary>Tab-specific presentation; commands are forwarded to the shared application workflow.</summary>
public partial class DeckAnalyzerView : UserControl
{
    public DeckAnalyzerView() => InitializeComponent();
    public event EventHandler<RoutedEventArgs>? AddDeckCardRequested;
    public event EventHandler<RoutedEventArgs>? RemoveDeckCardRequested;
    private void AddDeckCard_Click(object sender, RoutedEventArgs e) => AddDeckCardRequested?.Invoke(sender, e);
    private void RemoveDeckCard_Click(object sender, RoutedEventArgs e) => RemoveDeckCardRequested?.Invoke(sender, e);
    public event EventHandler<RoutedEventArgs>? AnalyzeDeck_ClickRequested;
    private void AnalyzeDeck_Click(object sender, RoutedEventArgs e) => AnalyzeDeck_ClickRequested?.Invoke(sender, e);
    public event EventHandler<RoutedEventArgs>? LoadCurrentDeckFromSave_ClickRequested;
    private void LoadCurrentDeckFromSave_Click(object sender, RoutedEventArgs e) => LoadCurrentDeckFromSave_ClickRequested?.Invoke(sender, e);
    public event EventHandler<RoutedEventArgs>? CancelDeck_ClickRequested;
    private void CancelDeck_Click(object sender, RoutedEventArgs e) => CancelDeck_ClickRequested?.Invoke(sender, e);
    public event EventHandler<RoutedEventArgs>? ClearDeck_ClickRequested;
    private void ClearDeck_Click(object sender, RoutedEventArgs e) => ClearDeck_ClickRequested?.Invoke(sender, e);
}
