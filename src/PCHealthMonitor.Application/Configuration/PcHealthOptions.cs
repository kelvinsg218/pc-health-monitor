using PCHealthMonitor.Domain.Health;

namespace PCHealthMonitor.Application.Configuration;

/// <summary>
/// All configurable settings, bound from the "PcHealth" section of appsettings.json
/// (and PCHEALTH_ environment variables). Every value has a working default.
/// </summary>
public sealed class PcHealthOptions
{
    public const string SectionName = "PcHealth";

    public ThresholdOptions Thresholds { get; set; } = new();

    public ScanOptions Scan { get; set; } = new();

    public MonitorOptions Monitor { get; set; } = new();

    public ProcessOptions Processes { get; set; } = new();

    public HistoryOptions History { get; set; } = new();

    public PathOptions Paths { get; set; } = new();

    /// <summary>Returns every validation error (empty when the configuration is valid).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        errors.AddRange(Thresholds.Cpu.Validate("Thresholds.Cpu", 0, 100));
        errors.AddRange(Thresholds.Memory.Validate("Thresholds.Memory", 0, 100));
        errors.AddRange(Thresholds.Storage.Validate("Thresholds.Storage", 0, 100));
        errors.AddRange(Thresholds.Temperature.Validate("Thresholds.Temperature", 0, 150));

        RequireRange(errors, "Thresholds.UptimeNoticeDays", Thresholds.UptimeNoticeDays, 0, 3650);

        RequireRange(errors, "Scan.DurationSeconds", Scan.DurationSeconds, 1, 600);
        RequireRange(errors, "Scan.SampleIntervalSeconds", Scan.SampleIntervalSeconds, 0.25, 60);
        if (double.IsFinite(Scan.SampleIntervalSeconds) && double.IsFinite(Scan.DurationSeconds)
            && Scan.SampleIntervalSeconds > Scan.DurationSeconds)
        {
            errors.Add($"Scan.SampleIntervalSeconds ({Scan.SampleIntervalSeconds}) cannot be greater than Scan.DurationSeconds ({Scan.DurationSeconds}).");
        }

        RequireRange(errors, "Scan.StatusCpuSampleSeconds", Scan.StatusCpuSampleSeconds, 0.25, 10);
        RequireRange(errors, "Monitor.RefreshIntervalSeconds", Monitor.RefreshIntervalSeconds, 0.5, 3600);
        RequireRange(errors, "Processes.TopCount", Processes.TopCount, 1, 100);
        RequireRange(errors, "Processes.CpuSampleSeconds", Processes.CpuSampleSeconds, 0.25, 30);
        RequireRange(errors, "History.RetentionDays", History.RetentionDays, 0, 36500);
        RequireRange(errors, "History.DefaultListLimit", History.DefaultListLimit, 1, 1000);

        RequirePath(errors, "Paths.DataDirectory", Paths.DataDirectory);
        RequirePath(errors, "Paths.ReportsDirectory", Paths.ReportsDirectory);
        RequirePath(errors, "Paths.LogsDirectory", Paths.LogsDirectory);
        return errors;
    }

    /// <summary>Throws <see cref="InvalidConfigurationException"/> when the configuration is invalid.</summary>
    public void EnsureValid()
    {
        var errors = Validate();
        if (errors.Count > 0)
        {
            throw new InvalidConfigurationException(errors);
        }
    }

    private static void RequireRange(List<string> errors, string name, double value, double min, double max)
    {
        if (!double.IsFinite(value) || value < min || value > max)
        {
            errors.Add($"{name} must be between {min} and {max} (was {value}).");
        }
    }

    private static void RequirePath(List<string> errors, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{name} cannot be empty.");
        }
        else if (value.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            errors.Add($"{name} contains invalid path characters.");
        }
    }
}

/// <summary>
/// Classification thresholds. Percentages for CPU/memory/storage, °C for temperature.
/// These are product defaults, not universal truths.
/// </summary>
public sealed class ThresholdOptions
{
    public double CpuWarning { get; set; } = 75;

    public double CpuCritical { get; set; } = 90;

    public double MemoryWarning { get; set; } = 80;

    public double MemoryCritical { get; set; } = 92;

    public double StorageWarning { get; set; } = 80;

    public double StorageCritical { get; set; } = 90;

    /// <summary>Applied to firmware (ACPI) thermal zone readings, when available.</summary>
    public double TemperatureWarning { get; set; } = 85;

    public double TemperatureCritical { get; set; } = 95;

    /// <summary>Uptime (days) from which an informational note is shown. 0 disables it. Never changes health.</summary>
    public double UptimeNoticeDays { get; set; } = 30;

    public ThresholdPair Cpu => new(CpuWarning, CpuCritical);

    public ThresholdPair Memory => new(MemoryWarning, MemoryCritical);

    public ThresholdPair Storage => new(StorageWarning, StorageCritical);

    public ThresholdPair Temperature => new(TemperatureWarning, TemperatureCritical);
}

public sealed class ScanOptions
{
    /// <summary>Length of the CPU sampling window of a full scan.</summary>
    public double DurationSeconds { get; set; } = 5;

    /// <summary>Interval of each CPU sample inside the scan window.</summary>
    public double SampleIntervalSeconds { get; set; } = 1;

    /// <summary>Length of the single CPU measurement used by the quick "status" command.</summary>
    public double StatusCpuSampleSeconds { get; set; } = 1;
}

public sealed class MonitorOptions
{
    public double RefreshIntervalSeconds { get; set; } = 2;
}

public sealed class ProcessOptions
{
    public int TopCount { get; set; } = 10;

    /// <summary>Window used to measure per-process CPU in the "processes" command.</summary>
    public double CpuSampleSeconds { get; set; } = 1;
}

public sealed class HistoryOptions
{
    /// <summary>Scans older than this are deleted after a new scan is saved. 0 keeps everything.</summary>
    public int RetentionDays { get; set; } = 365;

    public int DefaultListLimit { get; set; } = 20;
}

/// <summary>Directories are resolved relative to the current working directory when not absolute.</summary>
public sealed class PathOptions
{
    public string DataDirectory { get; set; } = "data";

    public string ReportsDirectory { get; set; } = "reports";

    public string LogsDirectory { get; set; } = "logs";

    public string DatabaseFileName { get; set; } = "pchealth.db";
}

public sealed class InvalidConfigurationException(IReadOnlyList<string> errors)
    : Exception("Invalid configuration: " + string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
