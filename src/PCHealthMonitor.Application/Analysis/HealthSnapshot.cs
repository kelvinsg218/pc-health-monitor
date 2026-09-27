using PCHealthMonitor.Domain.Health;
using PCHealthMonitor.Domain.Machine;
using PCHealthMonitor.Domain.Metrics;

namespace PCHealthMonitor.Application.Analysis;

/// <summary>A non-fatal problem found while collecting a metric (shown to the user without stack traces).</summary>
public sealed record CollectionIssue(HealthComponent Component, string Message);

/// <summary>
/// Everything collected in one check. Any metric can be null when it could not be collected;
/// the analyzer treats that as UNKNOWN, never as a failure of the machine.
/// </summary>
public sealed record HealthSnapshot(
    SystemInfo? System,
    CpuMetrics? Cpu,
    MemoryMetrics? Memory,
    StorageSnapshot? Storage,
    TemperatureReading? Temperature,
    ProcessSummary? Processes,
    IReadOnlyList<CollectionIssue> Issues)
{
    public static HealthSnapshot Empty { get; } = new(null, null, null, null, null, null, []);
}
