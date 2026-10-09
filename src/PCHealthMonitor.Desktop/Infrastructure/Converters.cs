using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PCHealthMonitor.Desktop.Infrastructure;

/// <summary>Maps a status label (OK, WARNING, CRITICAL, UNKNOWN, HEALTHY, ATTENTION, INFO) to a theme brush.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as string)?.ToUpperInvariant() switch
        {
            "OK" or "HEALTHY" => "AccentBrush",
            "WARNING" or "ATTENTION" => "WarningBrush",
            "CRITICAL" => "CriticalBrush",
            "INFO" => "MutedBrush",
            _ => "UnknownBrush",
        };

        var brush = System.Windows.Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
        if (parameter is string p && p == "soft" && brush is SolidColorBrush solid)
        {
            // Translucent background for pills.
            var c = solid.Color;
            var soft = new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B));
            soft.Freeze();
            return soft;
        }

        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true → Visible. With parameter "invert": true → Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter as string == "invert")
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Non-empty string / non-null → Visible.</summary>
public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value switch
        {
            null => false,
            string s => s.Length > 0,
            int i => i > 0,
            _ => true,
        };
        if (parameter as string == "invert")
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Displays a status label (OK, WARNING, …) in Brazilian Portuguese.</summary>
public sealed class StatusTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => PtBr.Status(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
