using PCHealthMonitor.Application.Processes;
using PCHealthMonitor.Domain.Metrics;
using PCHealthMonitor.Tests.Fakes;

namespace PCHealthMonitor.Tests.Processes;

public class ProcessAnalysisTests
{
    private const long Mb = 1024 * 1024;

    private static ProcessSnapshot Snap(double atSeconds, params ProcessSample[] processes) =>
        new(TimeSpan.FromSeconds(atSeconds), processes);

    private static ProcessSample P(int pid, string name, long? mb, double? cpuSeconds) =>
        new(pid, name, mb is null ? null : mb * Mb, cpuSeconds is null ? null : TimeSpan.FromSeconds(cpuSeconds.Value));

    [Fact]
    public void Cpu_percent_uses_cpu_time_delta_elapsed_time_and_logical_processors()
    {
        // 2 s of CPU time over 1 s wall time on 4 logical processors = 50% of the machine
        var first = Snap(10, P(100, "app", 10, 5));
        var second = Snap(11, P(100, "app", 10, 7));

        var usage = Assert.Single(ProcessCpuCalculator.Calculate(first, second, logicalProcessors: 4));

        Assert.Equal(50, usage.CpuPercent!.Value, precision: 6);
    }

    [Fact]
    public void Cpu_percent_is_clamped_to_100()
    {
        var usage = Assert.Single(ProcessCpuCalculator.Calculate(Snap(0, P(1, "a", 1, 0)), Snap(1, P(1, "a", 1, 50)), 4));

        Assert.Equal(100, usage.CpuPercent);
    }

    [Fact]
    public void Inaccessible_process_has_null_cpu_but_keeps_memory()
    {
        var first = Snap(0, P(4, "System", 1, null), P(10, "ok", 5, 1));
        var second = Snap(1, P(4, "System", 1, null), P(10, "ok", 5, 1.5));

        var usages = ProcessCpuCalculator.Calculate(first, second, 1);

        var protectedProcess = usages.Single(u => u.Pid == 4);
        Assert.Null(protectedProcess.CpuPercent);
        Assert.Equal(1 * Mb, protectedProcess.WorkingSetBytes);
        Assert.Equal(50, usages.Single(u => u.Pid == 10).CpuPercent!.Value, precision: 6);
    }

    [Fact]
    public void Process_that_ended_during_collection_is_dropped()
    {
        var first = Snap(0, P(10, "gone", 5, 1), P(11, "stays", 5, 1));
        var second = Snap(1, P(11, "stays", 5, 1.1));

        var usages = ProcessCpuCalculator.Calculate(first, second, 1);

        Assert.DoesNotContain(usages, u => u.Pid == 10);
        Assert.Single(usages);
    }

    [Fact]
    public void Process_started_during_collection_has_unknown_cpu()
    {
        var usages = ProcessCpuCalculator.Calculate(Snap(0), Snap(1, P(20, "new", 5, 3)), 1);

        Assert.Null(Assert.Single(usages).CpuPercent);
    }

    [Fact]
    public void Reused_pid_with_different_name_or_lower_cpu_time_is_not_computed()
    {
        var first = Snap(0, P(30, "old", 5, 100), P(31, "same", 5, 100));
        var second = Snap(1, P(30, "new", 5, 101), P(31, "same", 5, 2));

        var usages = ProcessCpuCalculator.Calculate(first, second, 1);

        Assert.All(usages, u => Assert.Null(u.CpuPercent));
    }

    [Fact]
    public void Idle_process_pid_zero_is_excluded()
    {
        var usages = ProcessCpuCalculator.Calculate(Snap(0, P(0, "Idle", 0, 0)), Snap(1, P(0, "Idle", 0, 7)), 8);

        Assert.Empty(usages);
    }

    [Fact]
    public void Zero_or_negative_elapsed_time_gives_unknown_cpu()
    {
        var usages = ProcessCpuCalculator.Calculate(Snap(5, P(1, "a", 1, 1)), Snap(5, P(1, "a", 1, 2)), 1);

        Assert.Null(Assert.Single(usages).CpuPercent);
    }

    [Fact]
    public void Invalid_negative_memory_is_treated_as_unavailable()
    {
        var usages = ProcessCpuCalculator.Calculate(Snap(0, P(1, "a", -1, 1)), Snap(1, P(1, "a", -1, 1)), 1);

        Assert.Null(Assert.Single(usages).WorkingSetBytes);
    }

    [Fact]
    public void Summary_orders_by_memory_descending_and_skips_unavailable()
    {
        var usages = new List<ProcessUsage>
        {
            new(1, "small", 10 * Mb, 1),
            new(2, "big", 900 * Mb, 2),
            new(3, "unknown", null, 3),
            new(4, "medium", 300 * Mb, null),
        };

        var summary = ProcessCpuCalculator.Summarize(usages, top: 10, TimeSpan.FromSeconds(1));

        Assert.Equal(["big", "medium", "small"], summary.TopByMemory.Select(p => p.Name));
        Assert.Equal(1, summary.MemoryUnavailableCount);
    }

    [Fact]
    public void Summary_orders_by_cpu_descending_and_skips_unavailable()
    {
        var usages = new List<ProcessUsage>
        {
            new(1, "low", 10 * Mb, 1.5),
            new(2, "high", 10 * Mb, 42),
            new(3, "denied", 10 * Mb, null),
            new(4, "mid", 10 * Mb, 12),
        };

        var summary = ProcessCpuCalculator.Summarize(usages, top: 10, TimeSpan.FromSeconds(1));

        Assert.Equal(["high", "mid", "low"], summary.TopByCpu.Select(p => p.Name));
        Assert.Equal(1, summary.CpuUnavailableCount);
        Assert.Equal(4, summary.TotalProcesses);
    }

    [Fact]
    public void Summary_limits_to_top_n()
    {
        var usages = Enumerable.Range(1, 50).Select(i => new ProcessUsage(i, $"p{i}", i * Mb, i / 10.0)).ToList();

        var summary = ProcessCpuCalculator.Summarize(usages, top: 3, TimeSpan.FromSeconds(1));

        Assert.Equal([50, 49, 48], summary.TopByMemory.Select(p => p.Pid));
        Assert.Equal([50, 49, 48], summary.TopByCpu.Select(p => p.Pid));
    }

    [Fact]
    public void Summary_rejects_invalid_top()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProcessCpuCalculator.Summarize([], 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Service_measures_between_two_snapshots()
    {
        var collector = new FakeProcessCollector(
            Snap(0, P(10, "busy", 100, 0), P(11, "idle", 200, 0)),
            Snap(1, P(10, "busy", 100, 4), P(11, "idle", 200, 0)));
        var service = new ProcessService(collector, new FakeCpuCollector(0) { Info = new CpuInfo("x", 8) });

        var summary = await service.GetTopProcessesAsync(TimeSpan.Zero, 5, CancellationToken.None);

        Assert.Equal("idle", summary.TopByMemory[0].Name);
        Assert.Equal("busy", summary.TopByCpu[0].Name);
        Assert.Equal(50, summary.TopByCpu[0].CpuPercent!.Value, precision: 6);
        Assert.Equal(TimeSpan.FromSeconds(1), summary.CpuSampleWindow);
    }
}
