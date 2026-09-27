using PCHealthMonitor.Domain.Health;

namespace PCHealthMonitor.Application.History;

/// <summary>
/// Observed change of a percentage metric between two scans, in percentage points
/// (absolute difference: 70% → 80% is +10 percentage points, not +10% nor +14.3%).
/// </summary>
public sealed record MetricChange(string Metric, double? Previous, double? Current)
{
    public double? DeltaPoints => Previous is { } p && Current is { } c ? c - p : null;
}

public sealed record ScanComparison(ScanRecord Previous, ScanRecord Current, IReadOnlyList<MetricChange> Changes)
{
    public bool OverallChanged => Previous.OverallHealth != Current.OverallHealth;

    public string OverallTransition => $"{Previous.OverallHealth.ToLabel()} → {Current.OverallHealth.ToLabel()}";
}

/// <summary>Simple arithmetic comparison of two scans. Describes observed change only — no prediction.</summary>
public static class HistoryComparer
{
    public static ScanComparison Compare(ScanRecord previous, ScanRecord current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var changes = new List<MetricChange>
        {
            new("CPU average", previous.CpuAverage, current.CpuAverage),
            new("CPU peak", previous.CpuPeak, current.CpuPeak),
            new("Memory usage", previous.MemoryUsagePercent, current.MemoryUsagePercent),
        };

        var driveNames = previous.Drives.Select(d => d.Name)
            .Concat(current.Drives.Select(d => d.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        foreach (var name in driveNames)
        {
            var before = previous.Drives.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
            var after = current.Drives.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
            changes.Add(new MetricChange($"Drive {name} usage", before?.UsagePercent, after?.UsagePercent));
        }

        return new ScanComparison(previous, current, changes);
    }
}
