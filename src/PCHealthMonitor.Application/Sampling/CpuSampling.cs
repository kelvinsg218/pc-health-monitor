using PCHealthMonitor.Application.Abstractions;
using PCHealthMonitor.Domain.Metrics;

namespace PCHealthMonitor.Application.Sampling;

/// <summary>Cumulative system CPU times (100 ns units), as returned by GetSystemTimes.</summary>
/// <remarks>On Windows, kernel time includes idle time.</remarks>
public readonly record struct CpuTimes(long Idle, long Kernel, long User);

public static class CpuUsageCalculator
{
    /// <summary>
    /// Total CPU usage (0–100) between two readings: (kernel + user − idle) / (kernel + user).
    /// Returns NaN when the delta is empty or inconsistent (e.g. counters went backwards).
    /// </summary>
    public static double Calculate(CpuTimes previous, CpuTimes current)
    {
        var idle = current.Idle - previous.Idle;
        var kernel = current.Kernel - previous.Kernel;
        var user = current.User - previous.User;
        var total = kernel + user;

        if (total <= 0 || idle < 0 || kernel < 0 || user < 0 || idle > total)
        {
            return double.NaN;
        }

        return Math.Clamp((total - idle) * 100.0 / total, 0, 100);
    }
}

/// <summary>Takes several CPU usage samples over a window and computes min/max/average.</summary>
public sealed class CpuSampler(ICpuCollector collector)
{
    public static int SampleCount(TimeSpan duration, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be positive.");
        }

        return Math.Max(1, (int)Math.Round(duration / interval, MidpointRounding.AwayFromZero));
    }

    /// <exception cref="MetricUnavailableException">No valid sample could be taken.</exception>
    public async Task<CpuMetrics> SampleAsync(
        TimeSpan duration,
        TimeSpan interval,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var count = SampleCount(duration, interval);
        var samples = new List<double>(count);
        for (var i = 0; i < count; i++)
        {
            samples.Add(await collector.MeasureUsageAsync(interval, cancellationToken).ConfigureAwait(false));
            progress?.Report(i + 1);
        }

        var statistics = SampleStatistics.FromSamples(samples)
            ?? throw new MetricUnavailableException("No valid CPU usage sample could be taken.");
        return new CpuMetrics(collector.GetInfo(), statistics, interval);
    }
}

/// <summary>Raised when a metric cannot be produced from the collected data.</summary>
public sealed class MetricUnavailableException(string message) : Exception(message);
