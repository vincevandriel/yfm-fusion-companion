using System.Windows;
using System.Windows.Controls;

namespace YfmCompanion.Desktop.Views;

/// <summary>Tab-specific presentation; commands are forwarded to the shared application workflow.</summary>
public partial class LiveDuelView : UserControl
{
    public LiveDuelView() => InitializeComponent();
    public event EventHandler<RoutedEventArgs>? ToggleInspector_ClickRequested;
    private void ToggleInspector_Click(object sender, RoutedEventArgs e) => ToggleInspector_ClickRequested?.Invoke(sender, e);
    public event EventHandler<SelectionChangedEventArgs>? LiveCard_SelectionChangedRequested;
    private void LiveCard_SelectionChanged(object sender, SelectionChangedEventArgs e) => LiveCard_SelectionChangedRequested?.Invoke(sender, e);
}
