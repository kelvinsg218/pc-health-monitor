using PCHealthMonitor.Application.Analysis;
using PCHealthMonitor.Domain.Machine;
using PCHealthMonitor.Domain.Metrics;

namespace PCHealthMonitor.Application.Monitoring;

/// <summary>
/// Turns a live monitor refresh into a <see cref="HealthSnapshot"/> so that live views are classified by
/// the same <see cref="HealthAnalyzer"/> rules as status/scan. The CPU value of a refresh is a single
/// sample over the refresh interval. Temperature and system info are passed in because they change slowly
/// and are refreshed less often by the caller.
/// </summary>
public static class LiveHealth
{
    public static HealthSnapshot ToSnapshot(
        MonitorTick tick,
        CpuInfo cpuInfo,
        TimeSpan refreshInterval,
        SystemInfo? system,
        TemperatureReading? temperature)
    {
        ArgumentNullException.ThrowIfNull(tick);
        ArgumentNullException.ThrowIfNull(cpuInfo);

        CpuMetrics? cpu = tick.CpuPercent is { } value && SampleStatistics.FromSamples([value]) is { } stats
            ? new CpuMetrics(cpuInfo, stats, refreshInterval)
            : null;

        var updatedSystem = system is not null && tick.Uptime is { } uptime
            ? system with { Uptime = uptime, Timestamp = tick.Timestamp }
            : system;

        return new HealthSnapshot(updatedSystem, cpu, tick.Memory, tick.Storage, temperature, null, []);
    }
}
