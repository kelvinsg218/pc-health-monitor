namespace PCHealthMonitor.Domain.Metrics;

/// <summary>Static CPU information. Name is null when it could not be read.</summary>
public sealed record CpuInfo(string? Name, int LogicalProcessorCount);

/// <summary>CPU information plus usage statistics measured over a sampling window.</summary>
public sealed record CpuMetrics(CpuInfo Info, SampleStatistics Usage, TimeSpan SampleInterval);

/// <summary>
/// Physical memory as reported by the OS. TotalBytes is the memory usable by Windows
/// (it can be lower than the installed RAM because of hardware/iGPU reservations).
/// </summary>
public sealed record MemoryMetrics(ulong TotalBytes, ulong AvailableBytes, ulong? InstalledBytes = null)
{
    public ulong UsedBytes => TotalBytes >= AvailableBytes ? TotalBytes - AvailableBytes : 0;

    public double UsagePercent => TotalBytes == 0 ? double.NaN : UsedBytes * 100.0 / TotalBytes;
}

/// <summary>Capacity information for one local drive/volume.</summary>
public sealed record DriveMetrics(string Name, string? FileSystem, string? Label, ulong TotalBytes, ulong FreeBytes)
{
    public ulong UsedBytes => TotalBytes >= FreeBytes ? TotalBytes - FreeBytes : 0;

    public double UsagePercent => TotalBytes == 0 ? double.NaN : UsedBytes * 100.0 / TotalBytes;
}

/// <summary>Result of a storage collection: readable drives and fixed drives that could not be read.</summary>
public sealed record StorageSnapshot(IReadOnlyList<DriveMetrics> Drives, IReadOnlyList<string> UnreadableDrives)
{
    public static StorageSnapshot Empty { get; } = new([], []);
}

/// <summary>One temperature sensor/zone value.</summary>
public sealed record TemperatureZone(string Name, double Celsius, bool? PassiveThrottling);

/// <summary>
/// Temperature data. When no trustworthy source exists, <see cref="IsAvailable"/> is false and
/// <see cref="Zones"/> is empty. Values are never invented.
/// </summary>
public sealed record TemperatureReading(string? Source, IReadOnlyList<TemperatureZone> Zones, string? UnavailableReason)
{
    public bool IsAvailable => Zones.Count > 0;

    public double? MaxCelsius => Zones.Count == 0 ? null : Zones.Max(z => z.Celsius);

    public bool ThrottlingReported => Zones.Any(z => z.PassiveThrottling == true);

    public static TemperatureReading Unavailable(string reason) => new(null, [], reason);
}

/// <summary>
/// Raw per-process data from one snapshot. Null values mean "not accessible"
/// (e.g. protected process) — never zero.
/// </summary>
public sealed record ProcessSample(int Pid, string Name, long? WorkingSetBytes, TimeSpan? TotalProcessorTime);

/// <summary>Processes captured at a monotonic point in time (CapturedAt is relative, not wall-clock).</summary>
public sealed record ProcessSnapshot(TimeSpan CapturedAt, IReadOnlyList<ProcessSample> Processes);

/// <summary>Per-process usage after analysis. CpuPercent is relative to the whole machine (0–100).</summary>
public sealed record ProcessUsage(int Pid, string Name, long? WorkingSetBytes, double? CpuPercent);

/// <summary>Top processes by memory and CPU.</summary>
public sealed record ProcessSummary(
    IReadOnlyList<ProcessUsage> TopByMemory,
    IReadOnlyList<ProcessUsage> TopByCpu,
    int TotalProcesses,
    int CpuUnavailableCount,
    int MemoryUnavailableCount,
    TimeSpan CpuSampleWindow);
