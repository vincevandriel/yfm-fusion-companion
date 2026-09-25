using System.Windows;

namespace YfmCompanion.Desktop;

public partial class MainWindow
{
    private void AddDeckCard_Click(object sender, RoutedEventArgs e)
    {
        if (_deckAddPicker?.SelectedCard is not { } card) return;
        var slot = _deckPickers.FirstOrDefault(picker => picker.SelectedCard is null);
        if (slot is null) { DeckResultSummary.Text = "All 40 slots are filled. Remove a copy before adding another card."; return; }
        slot.SetCard(card);
        _deckAddPicker.Clear();
        _deckAddPicker.FocusInput();
    }
    private void RemoveDeckCard_Click(object sender, RoutedEventArgs e)
    {
        if (DeckAnalyzerPaneView.DeckTrayGallery.SelectedItem is not DeckCopyTile selected)
        { DeckResultSummary.Text = "Select a card tile, then remove one copy."; return; }
        _deckPickers.LastOrDefault(picker => picker.SelectedCard?.Id == selected.Card.Id)?.Clear();
    }
}
