using PCHealthMonitor.Application.Abstractions;
using PCHealthMonitor.Domain.Machine;
using PCHealthMonitor.Domain.Metrics;

namespace PCHealthMonitor.Tests.Fakes;

// Fakes replace every real collector: tests never depend on the CPU, RAM, disks,
// temperature or processes of the machine running them.

public sealed class FakeCpuCollector(params double[] samples) : ICpuCollector
{
    private readonly Queue<double> _samples = new(samples);

    public CpuInfo Info { get; set; } = new("Fake CPU 3000", 8);

    public int Calls { get; private set; }

    /// <summary>Invoked on each measurement (e.g. to cancel after N calls).</summary>
    public Action<int>? OnMeasure { get; set; }

    public Exception? Throw { get; set; }

    public CpuInfo GetInfo() => Info;

    public Task<double> MeasureUsageAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        OnMeasure?.Invoke(Calls);
        if (Throw is not null)
        {
            throw Throw;
        }

        // Repeat the last value once the queue is exhausted.
        var value = _samples.Count > 1 ? _samples.Dequeue() : _samples.Count == 1 ? _samples.Peek() : 0;
        return Task.FromResult(value);
    }
}

public sealed class FakeMemoryCollector(MemoryMetrics? metrics) : IMemoryCollector
{
    public int Calls { get; private set; }

    public MemoryMetrics Collect()
    {
        Calls++;
        return metrics ?? throw new InvalidOperationException("memory failure");
    }

    public static FakeMemoryCollector WithUsage(double percent, ulong total = 16UL * 1024 * 1024 * 1024)
    {
        var available = (ulong)(total * (100 - percent) / 100.0);
        return new FakeMemoryCollector(new MemoryMetrics(total, available));
    }
}

public sealed class FakeStorageCollector(StorageSnapshot? snapshot) : IStorageCollector
{
    public StorageSnapshot Collect() => snapshot ?? throw new IOException("storage failure");

    public static FakeStorageCollector WithDrive(string name, double usagePercent, ulong total = 500UL * 1024 * 1024 * 1024)
    {
        var free = (ulong)(total * (100 - usagePercent) / 100.0);
        return new FakeStorageCollector(new StorageSnapshot([new DriveMetrics(name, "NTFS", null, total, free)], []));
    }
}

public sealed class FakeTemperatureProvider(TemperatureReading? reading) : ITemperatureProvider
{
    public TemperatureReading Read() => reading ?? throw new InvalidOperationException("WMI failure");

    public static FakeTemperatureProvider Unavailable() => new(TemperatureReading.Unavailable("not exposed"));
}

public sealed class FakeProcessCollector(params ProcessSnapshot[] snapshots) : IProcessCollector
{
    private readonly Queue<ProcessSnapshot> _snapshots = new(snapshots);

    public ProcessSnapshot Capture() => _snapshots.Count > 0 ? _snapshots.Dequeue() : throw new InvalidOperationException("no snapshot");
}

public sealed class FakeSystemInfoCollector(SystemInfo? info = null) : ISystemInfoCollector
{
    public static SystemInfo Default { get; } = new(
        "TEST-PC", "Windows 11", "Pro", "24H2", "26100.1000", "x64", TimeSpan.FromDays(3.5), "tester",
        new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.FromHours(-3)));

    public SystemInfo Collect() => info ?? Default;

    public TimeSpan? GetUptime() => (info ?? Default).Uptime;
}

/// <summary>Deterministic clock.</summary>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void Advance(TimeSpan by) => Now += by;
}
