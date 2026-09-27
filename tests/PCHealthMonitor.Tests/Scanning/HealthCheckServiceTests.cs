using Microsoft.Extensions.Logging.Abstractions;
using PCHealthMonitor.Application.Analysis;
using PCHealthMonitor.Application.Configuration;
using PCHealthMonitor.Application.History;
using PCHealthMonitor.Application.Reports;
using PCHealthMonitor.Application.Scanning;
using PCHealthMonitor.Domain.Health;
using PCHealthMonitor.Domain.Metrics;
using PCHealthMonitor.Tests.Configuration;
using PCHealthMonitor.Tests.Fakes;
using PCHealthMonitor.Tests.Persistence;
using PCHealthMonitor.Infrastructure.Reports;

namespace PCHealthMonitor.Tests.Scanning;

public class HealthCheckServiceTests
{
    private static readonly ScanSettings Settings = new(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1), 5);

    private static ProcessSnapshot Snap(double at, double cpuSeconds) =>
        new(TimeSpan.FromSeconds(at), [new ProcessSample(42, "worker", 100 * 1024 * 1024, TimeSpan.FromSeconds(cpuSeconds))]);

    private static HealthCheckService Create(
        FakeCpuCollector? cpu = null,
        FakeMemoryCollector? memory = null,
        FakeStorageCollector? storage = null,
        FakeTemperatureProvider? temperature = null,
        FakeProcessCollector? processes = null) => new(
        new FakeSystemInfoCollector(),
        cpu ?? new FakeCpuCollector(20, 40, 60, 80),
        memory ?? FakeMemoryCollector.WithUsage(50),
        storage ?? FakeStorageCollector.WithDrive("C:", 60),
        temperature ?? FakeTemperatureProvider.Unavailable(),
        processes ?? new FakeProcessCollector(Snap(0, 1), Snap(4, 3)),
        new HealthAnalyzer(new ThresholdOptions()),
        new ManualTimeProvider(TestData.BaseTime),
        NullLogger<HealthCheckService>.Instance);

    [Fact]
    public async Task Full_scan_samples_cpu_and_measures_processes_over_the_window()
    {
        var cpu = new FakeCpuCollector(20, 40, 60, 80);

        var result = await Create(cpu).FullScanAsync(Settings, null, CancellationToken.None);

        Assert.True(result.IsFullScan);
        Assert.Equal(4, cpu.Calls);
        Assert.Equal(50, result.Snapshot.Cpu!.Usage.Average);
        Assert.Equal(80, result.Snapshot.Cpu.Usage.Peak);
        var worker = Assert.Single(result.Snapshot.Processes!.TopByCpu);
        Assert.Equal(6.25, worker.CpuPercent!.Value, precision: 6); // 2 s CPU / (4 s × 8 CPUs)
        Assert.Equal(OverallHealth.Healthy, result.Assessment.Overall);
        Assert.Equal("TEST-PC", result.Hostname);
    }

    [Fact]
    public async Task Unavailable_temperature_does_not_stop_the_scan()
    {
        var result = await Create().FullScanAsync(Settings, null, CancellationToken.None);

        Assert.Equal(HealthStatus.Ok, result.Assessment.StatusOf(HealthComponent.Cpu));
        Assert.Equal(HealthStatus.Ok, result.Assessment.StatusOf(HealthComponent.Memory));
        Assert.Equal(HealthStatus.Ok, result.Assessment.StatusOf(HealthComponent.Storage));
        Assert.Equal(HealthStatus.Unknown, result.Assessment.StatusOf(HealthComponent.Temperature));
        Assert.Equal(OverallHealth.Healthy, result.Assessment.Overall);
    }

    [Fact]
    public async Task Collector_exceptions_become_unknown_components_not_crashes()
    {
        var service = Create(
            cpu: new FakeCpuCollector(10) { Throw = new InvalidOperationException("boom") },
            memory: new FakeMemoryCollector(null),
            temperature: new FakeTemperatureProvider(null),
            processes: new FakeProcessCollector()); // throws on capture

        var result = await service.FullScanAsync(Settings, null, CancellationToken.None);

        Assert.Equal(HealthStatus.Unknown, result.Assessment.StatusOf(HealthComponent.Cpu));
        Assert.Equal(HealthStatus.Unknown, result.Assessment.StatusOf(HealthComponent.Memory));
        Assert.Equal(HealthStatus.Unknown, result.Assessment.StatusOf(HealthComponent.Temperature));
        Assert.Equal(HealthStatus.Ok, result.Assessment.StatusOf(HealthComponent.Storage));
        Assert.Null(result.Snapshot.Processes);
        Assert.True(result.Assessment.IsIncomplete);
        Assert.NotEqual(OverallHealth.Critical, result.Assessment.Overall);

        // Friendly messages only: no exception text or stack trace reaches the user.
        Assert.Contains(result.Assessment.Messages, m => m.Text == "CPU usage could not be collected.");
        Assert.DoesNotContain(result.Assessment.Messages, m => m.Text.Contains("boom", StringComparison.Ordinal)
            || m.Text.Contains("Exception", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Quick_status_takes_one_cpu_sample_and_no_processes()
    {
        var cpu = new FakeCpuCollector(33);

        var result = await Create(cpu).QuickStatusAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.False(result.IsFullScan);
        Assert.Equal(1, cpu.Calls);
        Assert.Equal(1, result.Snapshot.Cpu!.Usage.SampleCount);
        Assert.Null(result.Snapshot.Processes);
    }

    [Fact]
    public async Task Cancellation_propagates_from_the_scan()
    {
        using var cts = new CancellationTokenSource();
        var cpu = new FakeCpuCollector(10) { OnMeasure = n => { if (n == 2) cts.Cancel(); } };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(cpu).FullScanAsync(Settings, null, cts.Token));
    }

    [Fact]
    public async Task Workflow_saves_compares_and_writes_report()
    {
        using var database = new SqliteTestDatabase();
        using var dir = new TempDirectory();
        var options = new PcHealthOptions();
        HistoryService History() => new(database.CreateRepository(), options.History, new ManualTimeProvider(TestData.BaseTime));
        var writer = new FileReportWriter(Path.Combine(dir.Path, "reports"));

        var first = await new ScanWorkflow(Create(), History, writer, options, "1.0.0", NullLogger<ScanWorkflow>.Instance)
            .RunAsync(new ScanWorkflowRequest(Settings, SaveToHistory: true, WriteReport: false), null, CancellationToken.None);
        var second = await new ScanWorkflow(Create(memory: FakeMemoryCollector.WithUsage(70)), History, writer, options, "1.0.0", NullLogger<ScanWorkflow>.Instance)
            .RunAsync(new ScanWorkflowRequest(Settings, SaveToHistory: true, WriteReport: true), null, CancellationToken.None);

        Assert.NotNull(first.SavedScan);
        Assert.Null(first.Comparison);
        Assert.Null(first.Report);
        Assert.NotNull(second.Comparison);
        Assert.Equal(20, second.Comparison.Changes.Single(c => c.Metric == "Memory usage").DeltaPoints!.Value, precision: 1);
        Assert.True(File.Exists(second.Report!.JsonPath));
        Assert.Null(second.HistoryError);
    }

    [Fact]
    public async Task Workflow_keeps_the_result_when_history_fails()
    {
        var options = new PcHealthOptions();
        HistoryService Broken() => throw new InvalidOperationException("database locked");

        var outcome = await new ScanWorkflow(Create(), Broken, new ThrowingWriter(), options, "1.0.0", NullLogger<ScanWorkflow>.Instance)
            .RunAsync(new ScanWorkflowRequest(Settings, SaveToHistory: true, WriteReport: true), null, CancellationToken.None);

        Assert.NotNull(outcome.Result);
        Assert.Null(outcome.SavedScan);
        Assert.NotNull(outcome.HistoryError);
        Assert.DoesNotContain("database locked", outcome.HistoryError, StringComparison.Ordinal);
        Assert.NotNull(outcome.ReportError);
    }

    [Fact]
    public async Task Workflow_no_save_does_not_touch_history()
    {
        var created = false;
        HistoryService Factory()
        {
            created = true;
            throw new InvalidOperationException();
        }

        var outcome = await new ScanWorkflow(Create(), Factory, new ThrowingWriter(), new PcHealthOptions(), "1.0.0", NullLogger<ScanWorkflow>.Instance)
            .RunAsync(new ScanWorkflowRequest(Settings, SaveToHistory: false, WriteReport: false), null, CancellationToken.None);

        Assert.False(created);
        Assert.Null(outcome.HistoryError);
    }

    private sealed class ThrowingWriter : IReportWriter
    {
        public Task<ReportFiles> WriteAsync(ScanReport report, CancellationToken cancellationToken) =>
            throw new UnauthorizedAccessException("reports is read-only");
    }
}
