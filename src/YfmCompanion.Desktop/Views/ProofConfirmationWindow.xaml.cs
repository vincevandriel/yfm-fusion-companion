using System.Numerics;
using System.Windows;

namespace YfmCompanion.Desktop.Views;

public partial class ProofConfirmationWindow : Window
{
    internal ProofConfirmationWindow(Window owner, BigInteger capacity, string estimate, bool existing, bool restart)
    {
        InitializeComponent();
        Owner = owner;
        Width = Math.Min(560, owner.ActualWidth);
        MaxHeight = Math.Max(180, Math.Min(SystemParameters.WorkArea.Height, owner.ActualHeight));
        SpaceText.Text = $"This request has {capacity:N0} capacity-bounded 40-card deck vectors. Purchase constraints may reject some branches.";
        EstimateText.Text = estimate;
        CheckpointText.Text = existing
            ? restart ? "A new proof will start. The previous checkpoint will be preserved as a backup."
                : "An existing checkpoint was found. Its rules and frozen inputs must match before it can resume."
            : "A new durable proof checkpoint will be created.";
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
