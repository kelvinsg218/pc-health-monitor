using PCHealthMonitor.Application.Scanning;
using PCHealthMonitor.Domain.Health;

namespace PCHealthMonitor.Application.History;

/// <summary>A persisted scan, as read back from history. Timestamps are UTC.</summary>
public sealed record ScanRecord
{
    public int Id { get; init; }

    public required string Hostname { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset FinishedAt { get; init; }

    public string? WindowsVersion { get; init; }

    public double? CpuAverage { get; init; }

    public double? CpuPeak { get; init; }

    public double? CpuMinimum { get; init; }

    public int? CpuSampleCount { get; init; }

    public double? MemoryUsagePercent { get; init; }

    public long? MemoryTotalBytes { get; init; }

    public long? MemoryUsedBytes { get; init; }

    public double? TemperatureCelsius { get; init; }

    public long? UptimeSeconds { get; init; }

    public HealthStatus CpuStatus { get; init; }

    public HealthStatus MemoryStatus { get; init; }

    public HealthStatus StorageStatus { get; init; }

    public HealthStatus TemperatureStatus { get; init; }

    public OverallHealth OverallHealth { get; init; }

    public bool IsIncomplete { get; init; }

    public IReadOnlyList<ScanDriveRecord> Drives { get; init; } = [];

    public IReadOnlyList<DiagnosticMessage> Messages { get; init; } = [];

    /// <summary>Builds the record to persist from a completed scan. Only diagnostic essentials are kept.</summary>
    public static ScanRecord FromResult(HealthCheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var snapshot = result.Snapshot;
        var assessment = result.Assessment;
        var drives = snapshot.Storage?.Drives
            .Select(d => new ScanDriveRecord(
                d.Name,
                d.FileSystem,
                (long)d.TotalBytes,
                (long)d.FreeBytes,
                d.UsagePercent,
                assessment.DriveStatus(d.Name) ?? HealthStatus.Unknown))
            .ToList() ?? [];

        return new ScanRecord
        {
            Hostname = result.Hostname,
            StartedAt = result.StartedAt.ToUniversalTime(),
            FinishedAt = result.FinishedAt.ToUniversalTime(),
            WindowsVersion = snapshot.System is { } system
                ? $"{system.WindowsDescription} (build {system.Build ?? "?"})"
                : null,
            CpuAverage = snapshot.Cpu?.Usage.Average,
            CpuPeak = snapshot.Cpu?.Usage.Peak,
            CpuMinimum = snapshot.Cpu?.Usage.Minimum,
            CpuSampleCount = snapshot.Cpu?.Usage.SampleCount,
            MemoryUsagePercent = snapshot.Memory is { TotalBytes: > 0 } memory ? memory.UsagePercent : null,
            MemoryTotalBytes = snapshot.Memory is { } m1 ? (long)m1.TotalBytes : null,
            MemoryUsedBytes = snapshot.Memory is { } m2 ? (long)m2.UsedBytes : null,
            TemperatureCelsius = snapshot.Temperature?.MaxCelsius,
            UptimeSeconds = snapshot.System?.Uptime is { } uptime ? (long)uptime.TotalSeconds : null,
            CpuStatus = assessment.StatusOf(HealthComponent.Cpu),
            MemoryStatus = assessment.StatusOf(HealthComponent.Memory),
            StorageStatus = assessment.StatusOf(HealthComponent.Storage),
            TemperatureStatus = assessment.StatusOf(HealthComponent.Temperature),
            OverallHealth = assessment.Overall,
            IsIncomplete = assessment.IsIncomplete,
            Drives = drives,
            Messages = assessment.Messages.ToList(),
        };
    }
}

public sealed record ScanDriveRecord(string Name, string? FileSystem, long TotalBytes, long FreeBytes, double UsagePercent, HealthStatus Status);
