using PCHealthMonitor.Domain.Machine;
using PCHealthMonitor.Domain.Metrics;

namespace PCHealthMonitor.Application.Abstractions;

// Collectors only collect raw data. They never decide whether a value is OK/WARNING/CRITICAL;
// that is the job of HealthAnalyzer. Every collector can be replaced by a fake in tests.

public interface ICpuCollector
{
    CpuInfo GetInfo();

    /// <summary>
    /// Measures total CPU usage (0–100) over <paramref name="interval"/>.
    /// Returns NaN when the measurement is not valid.
    /// </summary>
    Task<double> MeasureUsageAsync(TimeSpan interval, CancellationToken cancellationToken);
}

public interface IMemoryCollector
{
    MemoryMetrics Collect();
}

public interface IStorageCollector
{
    StorageSnapshot Collect();
}

public interface ITemperatureProvider
{
    /// <summary>Never throws for "not available"; returns <see cref="TemperatureReading.Unavailable"/> instead.</summary>
    TemperatureReading Read();
}

public interface IProcessCollector
{
    /// <summary>Captures a snapshot of running processes. Inaccessible values are null.</summary>
    ProcessSnapshot Capture();
}

public interface ISystemInfoCollector
{
    SystemInfo Collect();

    TimeSpan? GetUptime();
}
