using PCHealthMonitor.Application.Abstractions;
using PCHealthMonitor.Domain.Metrics;

namespace PCHealthMonitor.Application.Processes;

public static class ProcessCpuCalculator
{
    /// <summary>
    /// Computes per-process CPU usage between two snapshots:
    /// Δ(process CPU time) / (Δ wall time × logical processors) × 100, clamped to 0–100.
    /// A process gets a null CPU value when it was not present in both snapshots, when either
    /// CPU time is inaccessible, or when the values are inconsistent (e.g. PID reused).
    /// Processes that exited between the snapshots are dropped. PID 0 (Idle) is excluded.
    /// </summary>
    public static IReadOnlyList<ProcessUsage> Calculate(ProcessSnapshot first, ProcessSnapshot second, int logicalProcessors)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var elapsed = second.CapturedAt - first.CapturedAt;
        var canComputeCpu = elapsed > TimeSpan.Zero && logicalProcessors > 0;

        var previous = new Dictionary<int, ProcessSample>();
        foreach (var sample in first.Processes)
        {
            previous.TryAdd(sample.Pid, sample);
        }

        var result = new List<ProcessUsage>(second.Processes.Count);
        foreach (var current in second.Processes)
        {
            if (current.Pid == 0)
            {
                continue;
            }

            double? cpu = null;
            if (canComputeCpu
                && previous.TryGetValue(current.Pid, out var before)
                && string.Equals(before.Name, current.Name, StringComparison.OrdinalIgnoreCase)
                && before.TotalProcessorTime is { } t1
                && current.TotalProcessorTime is { } t2
                && t2 >= t1)
            {
                var percent = (t2 - t1).TotalSeconds / (elapsed.TotalSeconds * logicalProcessors) * 100.0;
                cpu = Math.Clamp(percent, 0, 100);
            }

            var workingSet = current.WorkingSetBytes is >= 0 ? current.WorkingSetBytes : null;
            result.Add(new ProcessUsage(current.Pid, current.Name, workingSet, cpu));
        }

        return result;
    }

    public static ProcessSummary Summarize(IReadOnlyList<ProcessUsage> usages, int top, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(usages);
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);

        var byMemory = usages
            .Where(p => p.WorkingSetBytes is not null)
            .OrderByDescending(p => p.WorkingSetBytes)
            .ThenBy(p => p.Pid)
            .Take(top)
            .ToList();

        var byCpu = usages
            .Where(p => p.CpuPercent is not null)
            .OrderByDescending(p => p.CpuPercent)
            .ThenByDescending(p => p.WorkingSetBytes ?? 0)
            .ThenBy(p => p.Pid)
            .Take(top)
            .ToList();

        return new ProcessSummary(
            byMemory,
            byCpu,
            usages.Count,
            usages.Count(p => p.CpuPercent is null),
            usages.Count(p => p.WorkingSetBytes is null),
            window);
    }
}

/// <summary>Measures top processes by memory and by CPU over a short window.</summary>
public sealed class ProcessService(IProcessCollector collector, ICpuCollector cpuCollector)
{
    public async Task<ProcessSummary> GetTopProcessesAsync(TimeSpan window, int top, CancellationToken cancellationToken)
    {
        var first = collector.Capture();
        if (window > TimeSpan.Zero)
        {
            await Task.Delay(window, cancellationToken).ConfigureAwait(false);
        }

        var second = collector.Capture();
        return Summarize(first, second, top);
    }

    public ProcessSummary Summarize(ProcessSnapshot first, ProcessSnapshot second, int top)
    {
        var logical = cpuCollector.GetInfo().LogicalProcessorCount;
        var usages = ProcessCpuCalculator.Calculate(first, second, logical);
        return ProcessCpuCalculator.Summarize(usages, top, second.CapturedAt - first.CapturedAt);
    }
}
