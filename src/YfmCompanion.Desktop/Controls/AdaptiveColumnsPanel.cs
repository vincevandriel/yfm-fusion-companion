using System.Windows;
using System.Windows.Controls;

namespace YfmCompanion.Desktop.Controls;

/// <summary>Equal-width columns that reflow into rows instead of forcing horizontal scrolling.</summary>
public sealed class AdaptiveColumnsPanel : Panel
{
    public static readonly DependencyProperty MinimumColumnWidthProperty = DependencyProperty.Register(
        nameof(MinimumColumnWidth), typeof(double), typeof(AdaptiveColumnsPanel),
        new FrameworkPropertyMetadata(280d, FrameworkPropertyMetadataOptions.AffectsMeasure),
        value => value is double number && double.IsFinite(number) && number > 0);
    public static readonly DependencyProperty MaximumColumnsProperty = DependencyProperty.Register(
        nameof(MaximumColumns), typeof(int), typeof(AdaptiveColumnsPanel),
        new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsMeasure), value => value is int number && number > 0);
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(AdaptiveColumnsPanel),
        new FrameworkPropertyMetadata(10d, FrameworkPropertyMetadataOptions.AffectsMeasure),
        value => value is double number && double.IsFinite(number) && number >= 0);
    public double MinimumColumnWidth { get => (double)GetValue(MinimumColumnWidthProperty); set => SetValue(MinimumColumnWidthProperty, value); }
    public int MaximumColumns { get => (int)GetValue(MaximumColumnsProperty); set => SetValue(MaximumColumnsProperty, value); }
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }

    private int Columns(double width) => double.IsInfinity(width) ? MaximumColumns
        : Math.Clamp((int)Math.Floor((width + Gap) / (MinimumColumnWidth + Gap)), 1, MaximumColumns);

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = Columns(availableSize.Width);
        var width = double.IsInfinity(availableSize.Width) ? MinimumColumnWidth
            : Math.Max(0, (availableSize.Width - Gap * (columns - 1)) / columns);
        var children = InternalChildren.Cast<UIElement>().Where(child => child.Visibility != Visibility.Collapsed).ToArray();
        foreach (var child in children) child.Measure(new Size(width, double.PositiveInfinity));
        var height = children.Chunk(columns).Sum(row => row.Max(child => child.DesiredSize.Height) + Gap);
        return new Size(double.IsInfinity(availableSize.Width) ? width * columns + Gap * (columns - 1) : availableSize.Width,
            Math.Max(0, height - Gap));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = Columns(finalSize.Width);
        var width = Math.Max(0, (finalSize.Width - Gap * (columns - 1)) / columns);
        var top = 0d;
        foreach (var row in InternalChildren.Cast<UIElement>().Where(child => child.Visibility != Visibility.Collapsed).Chunk(columns))
        {
            var height = row.Max(child => child.DesiredSize.Height);
            for (var column = 0; column < row.Length; column++)
                row[column].Arrange(new Rect(column * (width + Gap), top, width, height));
            top += height + Gap;
        }
        return finalSize;
    }
}
