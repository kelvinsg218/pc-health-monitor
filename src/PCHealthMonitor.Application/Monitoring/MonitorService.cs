using Microsoft.Extensions.Logging;
using PCHealthMonitor.Application.Abstractions;
using PCHealthMonitor.Application.Configuration;
using PCHealthMonitor.Domain.Health;
using PCHealthMonitor.Domain.Metrics;

namespace PCHealthMonitor.Application.Monitoring;

public sealed record DriveTick(string Name, double UsagePercent, HealthStatus Status);

/// <summary>One refresh of the monitor. Null metrics were unavailable in this refresh.</summary>
public sealed record MonitorTick(
    int Sequence,
    DateTimeOffset Timestamp,
    double? CpuPercent,
    HealthStatus CpuStatus,
    MemoryMetrics? Memory,
    HealthStatus MemoryStatus,
    IReadOnlyList<DriveTick> Drives,
    TimeSpan? Uptime);

/// <summary>
/// Live monitor loop. It never persists anything: it has no dependency on the history
/// repository, so leaving it open cannot create database records.
/// The CPU measurement itself spans the refresh interval, so the loop never spins without delay.
/// </summary>
public sealed class MonitorService(
    ICpuCollector cpuCollector,
    IMemoryCollector memoryCollector,
    IStorageCollector storageCollector,
    ISystemInfoCollector systemCollector,
    ThresholdOptions thresholds,
    TimeProvider timeProvider,
    ILogger<MonitorService> logger)
{
    private readonly HashSet<string> _loggedFailures = [];

    /// <summary>
    /// Runs until cancelled (Ctrl+C) or until <paramref name="maxTicks"/> refreshes were produced.
    /// Cancellation ends the loop cleanly (no exception). Returns the number of refreshes produced.
    /// </summary>
    public async Task<int> RunAsync(
        TimeSpan refreshInterval,
        Func<MonitorTick, CancellationToken, Task> onTick,
        int? maxTicks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onTick);
        if (refreshInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(refreshInterval), "Refresh interval must be positive.");
        }

        var ticks = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested && (maxTicks is null || ticks < maxTicks))
            {
                var tick = await CollectTickAsync(ticks + 1, refreshInterval, cancellationToken).ConfigureAwait(false);
                ticks++;
                await onTick(tick, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ctrl+C / cancellation: normal termination.
        }

        return ticks;
    }

    private async Task<MonitorTick> CollectTickAsync(int sequence, TimeSpan interval, CancellationToken cancellationToken)
    {
        double? cpu = null;
        try
        {
            var value = await cpuCollector.MeasureUsageAsync(interval, cancellationToken).ConfigureAwait(false);
            cpu = double.IsFinite(value) ? value : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOnce("cpu", ex);

            // Keep the refresh cadence even when the CPU measurement fails.
            await Task.Delay(interval, timeProvider, cancellationToken).ConfigureAwait(false);
        }

        var memory = Try("memory", memoryCollector.Collect);
        var storage = Try("storage", storageCollector.Collect);
        var uptime = Try("uptime", () => systemCollector.GetUptime());

        var drives = storage?.Drives
            .Select(d => new DriveTick(d.Name, d.UsagePercent, thresholds.Storage.Classify(d.UsagePercent)))
            .ToList() ?? [];

        return new MonitorTick(
            sequence,
            timeProvider.GetLocalNow(),
            cpu,
            thresholds.Cpu.Classify(cpu),
            memory,
            thresholds.Memory.Classify(memory is { TotalBytes: > 0 } m ? m.UsagePercent : null),
            drives,
            uptime);
    }

    private T? Try<T>(string key, Func<T?> collect)
    {
        try
        {
            return collect();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOnce(key, ex);
            return default;
        }
    }

    // Avoid flooding the log file: each failing metric is logged once per monitor session.
    private void LogOnce(string key, Exception ex)
    {
        if (_loggedFailures.Add(key))
        {
            logger.LogWarning(ex, "Monitor: {Metric} collection failed (further failures of this metric are not logged).", key);
        }
    }
}
