using PCHealthMonitor.Application.Analysis;
using PCHealthMonitor.Application.Configuration;
using PCHealthMonitor.Application.History;
using PCHealthMonitor.Application.Monitoring;
using PCHealthMonitor.Desktop;
using PCHealthMonitor.Desktop.ViewModels;
using PCHealthMonitor.Domain.Health;
using PCHealthMonitor.Domain.Metrics;
using PCHealthMonitor.Tests.Fakes;

namespace PCHealthMonitor.Tests.Desktop;

public class DesktopTests
{
    private static readonly CpuInfo Cpu = new("Fake CPU", 8);

    private static MonitorTick Tick(double? cpu, double memoryPercent, double diskPercent, TimeSpan? uptime = null)
    {
        const ulong gb = TestData.Gb;
        var memory = new MemoryMetrics(16 * gb, (ulong)(16 * gb * (100 - memoryPercent) / 100.0));
        var storage = new StorageSnapshot([new DriveMetrics("C:", "NTFS", null, 500 * gb, (ulong)(500 * gb * (100 - diskPercent) / 100.0))], []);
        return new MonitorTick(1, TestData.BaseTime, cpu, HealthStatus.Unknown, memory, HealthStatus.Unknown, [], uptime ?? TimeSpan.FromDays(2), storage);
    }

    [Fact]
    public void Live_snapshot_is_classified_by_the_same_analyzer_rules()
    {
        var snapshot = LiveHealth.ToSnapshot(Tick(cpu: 95, memoryPercent: 85, diskPercent: 40), Cpu, TimeSpan.FromSeconds(2), FakeSystemInfoCollector.Default, null);

        var assessment = new HealthAnalyzer(new ThresholdOptions()).Analyze(snapshot);

        Assert.Equal(HealthStatus.Critical, assessment.StatusOf(HealthComponent.Cpu));
        Assert.Equal(HealthStatus.Warning, assessment.StatusOf(HealthComponent.Memory));
        Assert.Equal(HealthStatus.Ok, assessment.StatusOf(HealthComponent.Storage));
        Assert.Equal(HealthStatus.Unknown, assessment.StatusOf(HealthComponent.Temperature));
        Assert.Equal(OverallHealth.Critical, assessment.Overall);
        Assert.Equal(1, snapshot.Cpu!.Usage.SampleCount);
        Assert.Equal(TimeSpan.FromSeconds(2), snapshot.Cpu.SampleInterval);
    }

    [Fact]
    public void Live_snapshot_with_unavailable_cpu_is_unknown_not_zero()
    {
        var snapshot = LiveHealth.ToSnapshot(Tick(cpu: null, memoryPercent: 40, diskPercent: 40), Cpu, TimeSpan.FromSeconds(2), null, null);

        Assert.Null(snapshot.Cpu);
        Assert.Equal(HealthStatus.Unknown, new HealthAnalyzer(new ThresholdOptions()).Analyze(snapshot).StatusOf(HealthComponent.Cpu));
    }

    [Fact]
    public void Live_snapshot_uses_the_current_uptime_and_keeps_temperature()
    {
        var temperature = new TemperatureReading("ACPI", [new TemperatureZone("TZ01", 40, false)], null);

        var snapshot = LiveHealth.ToSnapshot(Tick(10, 40, 40, uptime: TimeSpan.FromHours(5)), Cpu, TimeSpan.FromSeconds(2), FakeSystemInfoCollector.Default, temperature);

        Assert.Equal(TimeSpan.FromHours(5), snapshot.System!.Uptime);
        Assert.Same(temperature, snapshot.Temperature);
        Assert.Empty(snapshot.Issues);
    }

    [Fact]
    public async Task Monitor_ticks_carry_the_storage_snapshot_for_live_analysis()
    {
        MonitorTick? tick = null;
        var service = new MonitorService(new FakeCpuCollector(10), FakeMemoryCollector.WithUsage(50), FakeStorageCollector.WithDrive("C:", 70),
            new FakeSystemInfoCollector(), new ThresholdOptions(), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<MonitorService>.Instance);

        await service.RunAsync(TimeSpan.FromMilliseconds(1), (t, _) => { tick = t; return Task.CompletedTask; }, 1, CancellationToken.None);

        Assert.Equal("C:", Assert.Single(tick!.Storage!.Drives).Name);
    }

    [Theory]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "--data-dir" }, null)]
    [InlineData(new[] { "--other", "x" }, null)]
    public void Data_directory_argument_is_optional(string[] args, string? expected)
    {
        Assert.Equal(expected, App.ParseDataDirectory(args));
    }

    [Fact]
    public void Data_directory_argument_is_resolved_to_a_full_path()
    {
        var result = App.ParseDataDirectory(["--DATA-DIR", @"C:\Portable\PCHealth"]);

        Assert.Equal(@"C:\Portable\PCHealth", result);
    }

    [Fact]
    public void Default_data_folder_is_per_user_local_app_data()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCHealthMonitor");

        Assert.Equal(expected, DesktopHost.DefaultDataRoot);
    }

    [Fact]
    public void Comparison_rows_show_percentage_points_and_overall_transition()
    {
        var previous = TestData.Record(70, 74, TestData.BaseTime) with { Id = 1 };
        var current = TestData.Record(80, 81, TestData.BaseTime.AddDays(1)) with { Id = 2 };

        var rows = ChangeRowViewModel.From(HistoryComparer.Compare(previous, current)).ToList();

        Assert.Contains(rows, r => r.Metric == "Memory usage" && r.Change == "+10.0 percentage points" && r.Previous == "70.0%" && r.Current == "80.0%");
        Assert.Equal(new ChangeRowViewModel("Overall health", "HEALTHY", "ATTENTION", "changed"), rows[^1]);
    }

    [Fact]
    public void History_rows_show_the_fullest_drive_and_status()
    {
        var row = ScanRowViewModel.From(TestData.Record(93, 74, TestData.BaseTime) with { Id = 7 });

        Assert.Equal(7, row.Id);
        Assert.Equal("C: 74.0%", row.DiskMax);
        Assert.Equal("CRITICAL", row.Health);
        Assert.Equal("93.0%", row.Memory);
    }

    [Fact]
    public void Process_rows_show_unavailable_instead_of_zero()
    {
        var row = ProcessRowViewModel.From(new ProcessUsage(4, "System", null, null));

        Assert.Equal("Unavailable", row.Memory);
        Assert.Equal("Unavailable", row.Cpu);
    }
}
