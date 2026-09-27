using Microsoft.Extensions.Logging.Abstractions;
using PCHealthMonitor.Application.Configuration;
using PCHealthMonitor.Application.History;
using PCHealthMonitor.Application.Monitoring;
using PCHealthMonitor.Domain.Health;
using PCHealthMonitor.Tests.Fakes;

namespace PCHealthMonitor.Tests.Monitoring;

public class MonitorServiceTests
{
    private static MonitorService Create(Application.Abstractions.ICpuCollector cpu, FakeMemoryCollector? memory = null, FakeStorageCollector? storage = null) => new(
        cpu,
        memory ?? FakeMemoryCollector.WithUsage(71),
        storage ?? FakeStorageCollector.WithDrive("C:", 64),
        new FakeSystemInfoCollector(),
        new ThresholdOptions(),
        new ManualTimeProvider(TestData.BaseTime),
        NullLogger<MonitorService>.Instance);

    [Fact]
    public async Task Produces_the_requested_number_of_refreshes_and_classifies_values()
    {
        var ticks = new List<MonitorTick>();
        var service = Create(new FakeCpuCollector(32, 80, 95));

        var count = await service.RunAsync(TimeSpan.FromSeconds(2), (t, _) => { ticks.Add(t); return Task.CompletedTask; }, maxTicks: 3, CancellationToken.None);

        Assert.Equal(3, count);
        Assert.Equal([1, 2, 3], ticks.Select(t => t.Sequence));
        Assert.Equal([HealthStatus.Ok, HealthStatus.Warning, HealthStatus.Critical], ticks.Select(t => t.CpuStatus));
        Assert.Equal(71, ticks[0].Memory!.UsagePercent, precision: 3);
        Assert.Equal(HealthStatus.Ok, ticks[0].MemoryStatus);
        Assert.Equal("C:", Assert.Single(ticks[0].Drives).Name);
        Assert.Equal(TimeSpan.FromDays(3.5), ticks[0].Uptime);
    }

    [Fact]
    public async Task Cancellation_stops_the_loop_cleanly_without_throwing()
    {
        using var cts = new CancellationTokenSource();
        var ticks = 0;
        var service = Create(new FakeCpuCollector(10));

        var count = await service.RunAsync(
            TimeSpan.FromSeconds(1),
            (_, _) =>
            {
                if (++ticks == 4)
                {
                    cts.Cancel(); // simulates Ctrl+C
                }

                return Task.CompletedTask;
            },
            maxTicks: null,
            cts.Token);

        Assert.Equal(4, count);
    }

    [Fact]
    public async Task Cancellation_during_a_measurement_ends_cleanly()
    {
        using var cts = new CancellationTokenSource();
        var cpu = new FakeCpuCollector(10) { OnMeasure = n => { if (n == 3) cts.Cancel(); } };
        var service = Create(new CancellingCpu(cpu, cts));

        var count = await service.RunAsync(TimeSpan.FromSeconds(1), (_, _) => Task.CompletedTask, null, cts.Token);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Already_cancelled_token_produces_no_refresh()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var cpu = new FakeCpuCollector(10);

        var count = await Create(cpu).RunAsync(TimeSpan.FromSeconds(1), (_, _) => Task.CompletedTask, null, cts.Token);

        Assert.Equal(0, count);
        Assert.Equal(0, cpu.Calls);
    }

    [Fact]
    public async Task Failing_metrics_become_unavailable_and_the_monitor_keeps_running()
    {
        var ticks = new List<MonitorTick>();
        var service = Create(new FakeCpuCollector(double.NaN), new FakeMemoryCollector(null), new FakeStorageCollector(null));

        await service.RunAsync(TimeSpan.FromMilliseconds(1), (t, _) => { ticks.Add(t); return Task.CompletedTask; }, 2, CancellationToken.None);

        Assert.Equal(2, ticks.Count);
        Assert.All(ticks, t =>
        {
            Assert.Null(t.CpuPercent);
            Assert.Equal(HealthStatus.Unknown, t.CpuStatus);
            Assert.Null(t.Memory);
            Assert.Equal(HealthStatus.Unknown, t.MemoryStatus);
            Assert.Empty(t.Drives);
        });
    }

    [Fact]
    public void Monitor_has_no_dependency_on_history_persistence()
    {
        // Structural guarantee: the monitor cannot save scans because it never receives a repository.
        var parameterTypes = typeof(MonitorService).GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

        Assert.DoesNotContain(typeof(IScanRepository), parameterTypes);
        Assert.DoesNotContain(typeof(HistoryService), parameterTypes);
        Assert.DoesNotContain(parameterTypes, t => t.Namespace?.Contains("Persistence", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Monitor_refreshes_do_not_create_scan_records()
    {
        using var database = new Persistence.SqliteTestDatabase();
        var repository = database.CreateRepository();
        var service = Create(new FakeCpuCollector(50));

        await service.RunAsync(TimeSpan.FromMilliseconds(1), (_, _) => Task.CompletedTask, 25, CancellationToken.None);

        Assert.Empty(await repository.ListRecentAsync(100, CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_refresh_interval_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Create(new FakeCpuCollector(1)).RunAsync(TimeSpan.Zero, (_, _) => Task.CompletedTask, 1, CancellationToken.None));
    }

    /// <summary>Throws OperationCanceledException from inside the measurement, like Task.Delay does on Ctrl+C.</summary>
    private sealed class CancellingCpu(FakeCpuCollector inner, CancellationTokenSource cts) : Application.Abstractions.ICpuCollector
    {
        public Domain.Metrics.CpuInfo GetInfo() => inner.GetInfo();

        public async Task<double> MeasureUsageAsync(TimeSpan interval, CancellationToken cancellationToken)
        {
            var value = await inner.MeasureUsageAsync(interval, cancellationToken);
            cts.Token.ThrowIfCancellationRequested();
            return value;
        }
    }
}
