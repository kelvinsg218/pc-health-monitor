using Microsoft.Extensions.Logging;
using PCHealthMonitor.Application.Abstractions;
using PCHealthMonitor.Application.Analysis;
using PCHealthMonitor.Application.Processes;
using PCHealthMonitor.Application.Sampling;
using PCHealthMonitor.Domain.Health;
using PCHealthMonitor.Domain.Metrics;

namespace PCHealthMonitor.Application.Scanning;

/// <summary>Outcome of a status check or full scan.</summary>
public sealed record HealthCheckResult(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string Hostname,
    HealthSnapshot Snapshot,
    HealthAssessment Assessment,
    bool IsFullScan);

public sealed record ScanSettings(TimeSpan Duration, TimeSpan SampleInterval, int TopProcessCount);

/// <summary>
/// Orchestrates collection and analysis. Each collector is isolated: a failure becomes a
/// <see cref="CollectionIssue"/> (UNKNOWN) and never aborts the whole check.
/// </summary>
public sealed class HealthCheckService(
    ISystemInfoCollector systemCollector,
    ICpuCollector cpuCollector,
    IMemoryCollector memoryCollector,
    IStorageCollector storageCollector,
    ITemperatureProvider temperatureProvider,
    IProcessCollector processCollector,
    HealthAnalyzer analyzer,
    TimeProvider timeProvider,
    ILogger<HealthCheckService> logger)
{
    /// <summary>Quick check: one CPU measurement, memory, storage and temperature. No process sampling.</summary>
    public async Task<HealthCheckResult> QuickStatusAsync(TimeSpan cpuSample, CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetLocalNow();
        var issues = new List<CollectionIssue>();

        var system = Collect(HealthComponent.System, "System information", systemCollector.Collect, issues);
        var cpu = await CollectCpuAsync(cpuSample, cpuSample, null, issues, cancellationToken).ConfigureAwait(false);
        var memory = Collect(HealthComponent.Memory, "Memory information", memoryCollector.Collect, issues);
        var storage = Collect(HealthComponent.Storage, "Storage information", storageCollector.Collect, issues);
        var temperature = ReadTemperature(issues);

        var snapshot = new HealthSnapshot(system, cpu, memory, storage, temperature, null, issues);
        return Finish(startedAt, snapshot, isFullScan: false);
    }

    /// <summary>
    /// Full scan: CPU sampled several times over the configured window; process CPU measured
    /// over the same window (snapshot before and after), then memory, storage and temperature.
    /// </summary>
    public async Task<HealthCheckResult> FullScanAsync(
        ScanSettings settings,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var startedAt = timeProvider.GetLocalNow();
        var issues = new List<CollectionIssue>();

        progress?.Report("Collecting system information...");
        var system = Collect(HealthComponent.System, "System information", systemCollector.Collect, issues);

        var firstProcesses = Collect(HealthComponent.Processes, "Process list", processCollector.Capture, issues);

        var sampleCount = CpuSampler.SampleCount(settings.Duration, settings.SampleInterval);
        progress?.Report($"Sampling CPU ({sampleCount} samples, {settings.SampleInterval.TotalSeconds:0.##} s each)...");
        var cpu = await CollectCpuAsync(settings.Duration, settings.SampleInterval, null, issues, cancellationToken)
            .ConfigureAwait(false);

        ProcessSummary? processes = null;
        var secondProcesses = firstProcesses is null
            ? null
            : Collect(HealthComponent.Processes, "Process list", processCollector.Capture, issues);
        if (firstProcesses is not null && secondProcesses is not null)
        {
            processes = Collect(HealthComponent.Processes, "Process summary", () =>
            {
                var logical = cpu?.Info.LogicalProcessorCount ?? cpuCollector.GetInfo().LogicalProcessorCount;
                var usages = ProcessCpuCalculator.Calculate(firstProcesses, secondProcesses, logical);
                return ProcessCpuCalculator.Summarize(usages, settings.TopProcessCount, secondProcesses.CapturedAt - firstProcesses.CapturedAt);
            }, issues);
        }

        progress?.Report("Collecting memory, storage and temperature...");
        var memory = Collect(HealthComponent.Memory, "Memory information", memoryCollector.Collect, issues);
        var storage = Collect(HealthComponent.Storage, "Storage information", storageCollector.Collect, issues);
        var temperature = ReadTemperature(issues);

        var snapshot = new HealthSnapshot(system, cpu, memory, storage, temperature, processes, issues);
        return Finish(startedAt, snapshot, isFullScan: true);
    }

    private HealthCheckResult Finish(DateTimeOffset startedAt, HealthSnapshot snapshot, bool isFullScan)
    {
        var assessment = analyzer.Analyze(snapshot);
        var hostname = snapshot.System?.Hostname ?? Environment.MachineName;
        return new HealthCheckResult(startedAt, timeProvider.GetLocalNow(), hostname, snapshot, assessment, isFullScan);
    }

    private async Task<CpuMetrics?> CollectCpuAsync(
        TimeSpan duration,
        TimeSpan interval,
        IProgress<int>? progress,
        List<CollectionIssue> issues,
        CancellationToken cancellationToken)
    {
        try
        {
            return await new CpuSampler(cpuCollector).SampleAsync(duration, interval, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "CPU usage collection failed.");
            issues.Add(new CollectionIssue(HealthComponent.Cpu, "CPU usage could not be collected."));
            return null;
        }
    }

    private TemperatureReading ReadTemperature(List<CollectionIssue> issues)
    {
        var reading = Collect(HealthComponent.Temperature, "Temperature", temperatureProvider.Read, issues);
        if (reading is null)
        {
            return TemperatureReading.Unavailable("Temperature provider failed.");
        }

        if (!reading.IsAvailable)
        {
            logger.LogInformation("Temperature unavailable: {Reason}", reading.UnavailableReason);
        }

        return reading;
    }

    private T? Collect<T>(HealthComponent component, string what, Func<T> collect, List<CollectionIssue> issues)
        where T : class
    {
        try
        {
            return collect();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "{What} collection failed.", what);
            issues.Add(new CollectionIssue(component, $"{what} could not be collected."));
            return null;
        }
    }
}
