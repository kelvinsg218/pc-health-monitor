using System.Globalization;

namespace PCHealthMonitor.Application.Formatting;

/// <summary>Human-readable formatting. Sizes use binary units (1 GB = 1024³ bytes), as Windows does.</summary>
public static class Display
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static string Bytes(ulong bytes)
    {
        const double kb = 1024;
        const double mb = kb * 1024;
        const double gb = mb * 1024;
        const double tb = gb * 1024;

        return bytes switch
        {
            >= (ulong)tb => (bytes / tb).ToString("0.00", Culture) + " TB",
            >= (ulong)gb => (bytes / gb).ToString("0.0", Culture) + " GB",
            >= (ulong)mb => (bytes / mb).ToString("0.0", Culture) + " MB",
            >= (ulong)kb => (bytes / kb).ToString("0.0", Culture) + " KB",
            _ => bytes.ToString(Culture) + " B",
        };
    }

    public static string Bytes(long? bytes) => bytes is null or < 0 ? "Unavailable" : Bytes((ulong)bytes.Value);

    public static string Percent(double? value) =>
        value is null || !double.IsFinite(value.Value) ? "Unavailable" : value.Value.ToString("0.0", Culture) + "%";

    public static string Number(double value, string format = "0.#") => value.ToString(format, Culture);

    public static string Celsius(double? value) =>
        value is null ? "Unavailable" : value.Value.ToString("0.0", Culture) + " °C";

    /// <summary>Signed difference in percentage points, e.g. "+7.0 percentage points".</summary>
    public static string PercentagePoints(double delta)
    {
        var rounded = Math.Round(delta, 1);
        if (rounded == 0)
        {
            return "no change (0.0 percentage points)";
        }

        var sign = rounded > 0 ? "+" : "-";
        return sign + Math.Abs(rounded).ToString("0.0", Culture) + " percentage points";
    }

    /// <summary>"8 days 14 hours 32 minutes".</summary>
    public static string Uptime(TimeSpan? uptime)
    {
        if (uptime is null)
        {
            return "Unavailable";
        }

        var value = uptime.Value;
        if (value < TimeSpan.Zero)
        {
            return "Unavailable";
        }

        var parts = new List<string>();
        if (value.Days > 0)
        {
            parts.Add(Plural(value.Days, "day"));
        }

        if (value.Days > 0 || value.Hours > 0)
        {
            parts.Add(Plural(value.Hours, "hour"));
        }

        parts.Add(Plural(value.Minutes, "minute"));
        return string.Join(' ', parts);
    }

    /// <summary>"3d 14h 5m".</summary>
    public static string UptimeShort(TimeSpan? uptime)
    {
        if (uptime is null || uptime.Value < TimeSpan.Zero)
        {
            return "Unavailable";
        }

        var value = uptime.Value;
        return value.Days > 0
            ? $"{value.Days}d {value.Hours}h {value.Minutes}m"
            : $"{value.Hours}h {value.Minutes}m";
    }

    public static string LocalDateTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", Culture);

    public static string LocalDateTimeShort(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", Culture);

    private static string Plural(int value, string unit) => value == 1 ? $"1 {unit}" : $"{value} {unit}s";
}
