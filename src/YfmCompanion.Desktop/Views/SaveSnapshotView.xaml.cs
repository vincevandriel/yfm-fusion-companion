using System.Windows;
using System.Windows.Controls;

namespace YfmCompanion.Desktop.Views;

/// <summary>Tab-specific presentation; commands are forwarded to the shared application workflow.</summary>
public partial class SaveSnapshotView : UserControl
{
    public SaveSnapshotView() => InitializeComponent();
    public event EventHandler<RoutedEventArgs>? RefreshSaveSnapshot_ClickRequested;
    private void RefreshSaveSnapshot_Click(object sender, RoutedEventArgs e) => RefreshSaveSnapshot_ClickRequested?.Invoke(sender, e);
    public event EventHandler<RoutedEventArgs>? SelectSave_ClickRequested;
    private void SelectSave_Click(object sender, RoutedEventArgs e) => SelectSave_ClickRequested?.Invoke(sender, e);
    public event EventHandler<RoutedEventArgs>? ApplyDeckSnapshot_ClickRequested;
    private void ApplyDeckSnapshot_Click(object sender, RoutedEventArgs e) => ApplyDeckSnapshot_ClickRequested?.Invoke(sender, e);
    public event EventHandler<RoutedEventArgs>? ApplyOwnedSnapshot_ClickRequested;
    private void ApplyOwnedSnapshot_Click(object sender, RoutedEventArgs e) => ApplyOwnedSnapshot_ClickRequested?.Invoke(sender, e);
}

