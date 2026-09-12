using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YfmCompanion.Data;
using YfmCompanion.Engine;

namespace YfmCompanion.Desktop.Controls;

public partial class CardPicker : UserControl
{
    private CardSearchService? _search;
    private IReadOnlyList<Card> _suggestions = [];
    private bool _internalTextChange;

    public CardPicker()
    {
        InitializeComponent();
    }

    public Card? SelectedCard { get; private set; }

    public event EventHandler<Card?>? CardChanged;
    public event EventHandler? AdvanceRequested;

    public void Configure(string label, CardSearchService search)
    {
        InputLabel.Text = label;
        InputBox.AutomationName(label);
        _search = search;
    }

    public void FocusInput()
    {
        InputBox.Focus();
        InputBox.SelectAll();
    }

    public void Clear()
    {
        SelectedCard = null;
        _internalTextChange = true;
        InputBox.Clear();
        _internalTextChange = false;
        SuggestionPopup.IsOpen = false;
        CardChanged?.Invoke(this, null);
    }

    public void SetCard(Card card) => SelectCard(card);

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_internalTextChange || _search is null)
        {
            return;
        }

        if (SelectedCard is not null && !InputBox.Text.Equals(SelectedCard.Name, StringComparison.OrdinalIgnoreCase))
        {
            SelectedCard = null;
            CardChanged?.Invoke(this, null);
        }

        _suggestions = _search.Search(InputBox.Text);
        SuggestionList.ItemsSource = _suggestions;
        SuggestionPopup.IsOpen = InputBox.IsKeyboardFocusWithin && _suggestions.Count > 0;
    }

    private void InputBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_suggestions.Count > 0)
        {
            SuggestionPopup.IsOpen = true;
        }
    }

    private void InputBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!SuggestionList.IsKeyboardFocusWithin)
        {
            SuggestionPopup.IsOpen = false;
        }
    }

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SuggestionPopup.IsOpen = false;
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Tab || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return;
        }

        if (_suggestions.Count > 0)
        {
            SelectCard(_suggestions[0]);
            e.Handled = true;
            AdvanceRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SuggestionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(SuggestionList, e.OriginalSource as DependencyObject) is ListBoxItem item &&
            item.DataContext is Card card)
        {
            SelectCard(card);
            e.Handled = true;
        }
    }

    private void SelectCard(Card card)
    {
        SelectedCard = card;
        _internalTextChange = true;
        InputBox.Text = card.Name;
        InputBox.CaretIndex = InputBox.Text.Length;
        _internalTextChange = false;
        SuggestionPopup.IsOpen = false;
        CardChanged?.Invoke(this, card);
    }
}

internal static class AutomationExtensions
{
    public static void AutomationName(this UIElement element, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(element, name);
}
