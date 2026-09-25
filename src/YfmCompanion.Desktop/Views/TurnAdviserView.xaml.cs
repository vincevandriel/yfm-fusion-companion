using System.Windows;
using System.Windows.Controls;

namespace YfmCompanion.Desktop.Views;

/// <summary>Tab-specific presentation; commands are forwarded to the shared application workflow.</summary>
public partial class TurnAdviserView : UserControl
{
    public TurnAdviserView() => InitializeComponent();
    public event EventHandler<RoutedEventArgs>? AnalyzeTurn_ClickRequested;
    private void AnalyzeTurn_Click(object sender, RoutedEventArgs e) => AnalyzeTurn_ClickRequested?.Invoke(sender, e);
    public event EventHandler<RoutedEventArgs>? ClearTurn_ClickRequested;
    private void ClearTurn_Click(object sender, RoutedEventArgs e) => ClearTurn_ClickRequested?.Invoke(sender, e);
}

