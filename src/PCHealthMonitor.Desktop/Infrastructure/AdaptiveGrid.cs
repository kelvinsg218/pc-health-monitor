using System.Windows;
using System.Windows.Controls;

namespace PCHealthMonitor.Desktop.Infrastructure;

/// <summary>
/// Responsive grid: as many equal columns as fit with at least <see cref="MinItemWidth"/> each
/// (up to <see cref="MaxColumns"/>); every row is as tall as its tallest item.
/// </summary>
public sealed class AdaptiveGrid : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(280.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    private int Columns(double width)
    {
        var count = Math.Max(1, InternalChildren.Count);
        if (double.IsInfinity(width) || width <= 0)
        {
            return Math.Min(count, MaxColumns);
        }

        var fit = Math.Max(1, (int)(width / MinItemWidth));
        return Math.Max(1, Math.Min(Math.Min(fit, MaxColumns), count));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = Columns(availableSize.Width);
        var itemWidth = double.IsInfinity(availableSize.Width) ? MinItemWidth : availableSize.Width / columns;
        double total = 0, rowHeight = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if ((i + 1) % columns == 0 || i == InternalChildren.Count - 1)
            {
                total += rowHeight;
                rowHeight = 0;
            }
        }

        var width = double.IsInfinity(availableSize.Width) ? itemWidth * columns : availableSize.Width;
        return new Size(width, total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = Columns(finalSize.Width);
        var itemWidth = finalSize.Width / columns;
        double y = 0;
        for (var start = 0; start < InternalChildren.Count; start += columns)
        {
            var end = Math.Min(start + columns, InternalChildren.Count);
            double rowHeight = 0;
            for (var i = start; i < end; i++)
            {
                rowHeight = Math.Max(rowHeight, InternalChildren[i].DesiredSize.Height);
            }

            for (var i = start; i < end; i++)
            {
                InternalChildren[i].Arrange(new Rect((i - start) * itemWidth, y, itemWidth, rowHeight));
            }

            y += rowHeight;
        }

        return finalSize;
    }
}
