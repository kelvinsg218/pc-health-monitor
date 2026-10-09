using System.Windows;
using System.Windows.Media;

namespace PCHealthMonitor.Desktop.Infrastructure;

/// <summary>Small line chart of recent 0–100 values (real samples only; empty when there is no data).</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.SeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
        nameof(Capacity), typeof(int), typeof(Sparkline),
        new FrameworkPropertyMetadata(60, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public int Capacity
    {
        get => (int)GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;
        var w = ActualWidth;
        var h = ActualHeight;
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0x8C, 0x96, 0x9D)), 1);
        dc.DrawLine(gridPen, new Point(0, h - 0.5), new Point(w, h - 0.5));

        var values = Values;
        if (values is null || values.Count < 2 || w <= 0 || h <= 0)
        {
            return;
        }

        var step = w / Math.Max(1, Capacity - 1);
        var startX = w - ((values.Count - 1) * step);
        var line = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var ctx = line.Open())
        using (var f = fill.Open())
        {
            for (var i = 0; i < values.Count; i++)
            {
                var p = new Point(startX + (i * step), h - (Math.Clamp(values[i], 0, 100) / 100 * (h - 2)) - 1);
                if (i == 0)
                {
                    ctx.BeginFigure(p, false, false);
                    f.BeginFigure(new Point(p.X, h), true, true);
                    f.LineTo(p, false, false);
                }
                else
                {
                    ctx.LineTo(p, true, true);
                    f.LineTo(p, false, false);
                }
            }

            f.LineTo(new Point(w, h), false, false);
        }

        if (Stroke is SolidColorBrush solid)
        {
            var c = solid.Color;
            var area = new LinearGradientBrush(Color.FromArgb(0x55, c.R, c.G, c.B), Color.FromArgb(0x00, c.R, c.G, c.B), 90);
            dc.DrawGeometry(area, null, fill);
        }

        dc.DrawGeometry(null, new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round }, line);
    }
}
