using PCHealthMonitor.Application.Analysis;
using PCHealthMonitor.Application.Configuration;
using PCHealthMonitor.Domain.Health;
using PCHealthMonitor.Domain.Metrics;
using PCHealthMonitor.Tests.Fakes;

namespace PCHealthMonitor.Tests.Analysis;

public class HealthAnalyzerTests
{
    private const ulong Gb = 1024UL * 1024 * 1024;
    private readonly HealthAnalyzer _analyzer = new(new ThresholdOptions());

    private static CpuMetrics Cpu(double average, double? peak = null, int samples = 5) =>
        new(new CpuInfo("CPU", 8), new SampleStatistics(average, peak ?? average, average, samples), TimeSpan.FromSeconds(1));

    private static MemoryMetrics Memory(double percent) => new(100 * Gb, (ulong)((100 - percent) * Gb));

    private static StorageSnapshot Drives(params (string Name, double Percent)[] drives) =>
        new(drives.Select(d => new DriveMetrics(d.Name, "NTFS", null, 1000 * Gb, (ulong)((100 - d.Percent) * 10 * Gb))).ToList(), []);

    private static HealthSnapshot Snapshot(
        CpuMetrics? cpu = null,
        MemoryMetrics? memory = null,
        StorageSnapshot? storage = null,
        TemperatureReading? temperature = null,
        bool defaults = true) =>
        new(
            FakeSystemInfoCollector.Default,
            cpu ?? (defaults ? Cpu(10) : null),
            memory ?? (defaults ? Memory(40) : null),
            storage ?? (defaults ? Drives(("C:", 50)) : null),
            temperature ?? TemperatureReading.Unavailable("not exposed"),
            null,
            []);

    [Theory]
    [InlineData(0, HealthStatus.Ok)]
    [InlineData(74.9, HealthStatus.Ok)]
    [InlineData(74.99, HealthStatus.Ok)]
    [InlineData(75, HealthStatus.Warning)]
    [InlineData(89.9, HealthStatus.Warning)]
    [InlineData(90, HealthStatus.Critical)]
    [InlineData(100, HealthStatus.Critical)]
    public void Cpu_is_classified_by_average_with_inclusive_limits(double average, HealthStatus expected)
    {
        var result = _analyzer.Analyze(Snapshot(cpu: Cpu(average)));

        Assert.Equal(expected, result.StatusOf(HealthComponent.Cpu));
    }

    [Theory]
    [InlineData(79.9, HealthStatus.Ok)]
    [InlineData(80, HealthStatus.Warning)]
    [InlineData(91.9, HealthStatus.Warning)]
    [InlineData(92, HealthStatus.Critical)]
    public void Memory_is_classified_with_inclusive_limits(double percent, HealthStatus expected)
    {
        var result = _analyzer.Analyze(Snapshot(memory: Memory(percent)));

        Assert.Equal(expected, result.StatusOf(HealthComponent.Memory));
    }

    [Theory]
    [InlineData(79.9, HealthStatus.Ok)]
    [InlineData(80, HealthStatus.Warning)]
    [InlineData(89.9, HealthStatus.Warning)]
    [InlineData(90, HealthStatus.Critical)]
    public void Storage_is_classified_with_inclusive_limits(double percent, HealthStatus expected)
    {
        var result = _analyzer.Analyze(Snapshot(storage: Drives(("C:", percent))));

        Assert.Equal(expected, result.StatusOf(HealthComponent.Storage));
        Assert.Equal(expected, result.DriveStatus("C:"));
    }

    [Fact]
    public void Storage_component_takes_the_worst_drive()
    {
        var result = _analyzer.Analyze(Snapshot(storage: Drives(("C:", 50), ("D:", 95), ("E:", 85))));

        Assert.Equal(HealthStatus.Critical, result.StatusOf(HealthComponent.Storage));
        Assert.Equal(HealthStatus.Ok, result.DriveStatus("C:"));
        Assert.Equal(HealthStatus.Warning, result.DriveStatus("E:"));
        Assert.Contains(result.Messages, m => m.Severity == MessageSeverity.Critical && m.Text.Contains("Drive D:", StringComparison.Ordinal));
    }

    [Fact]
    public void Unreadable_drive_is_unknown_and_does_not_fail_the_other_drives()
    {
        var storage = new StorageSnapshot(Drives(("C:", 50)).Drives, ["D:"]);

        var result = _analyzer.Analyze(Snapshot(storage: storage));

        Assert.Equal(HealthStatus.Ok, result.StatusOf(HealthComponent.Storage));
        Assert.Equal(HealthStatus.Unknown, result.DriveStatus("D:"));
        Assert.True(result.IsIncomplete);
        Assert.Equal(OverallHealth.Healthy, result.Overall);
    }

    [Fact]
    public void Temperature_unavailable_is_unknown_with_honest_message()
    {
        var result = _analyzer.Analyze(Snapshot());

        Assert.Equal(HealthStatus.Unknown, result.StatusOf(HealthComponent.Temperature));
        Assert.Contains(result.Messages, m => m.Component == HealthComponent.Temperature
            && m.Severity == MessageSeverity.Info
            && m.Text.Contains("could not be obtained", StringComparison.Ordinal));
    }

    [Fact]
    public void Temperature_unknown_alone_does_not_make_the_machine_critical_or_attention()
    {
        var result = _analyzer.Analyze(Snapshot());

        Assert.Equal(OverallHealth.Healthy, result.Overall);
    }

    [Theory]
    [InlineData(40, HealthStatus.Ok)]
    [InlineData(85, HealthStatus.Warning)]
    [InlineData(95, HealthStatus.Critical)]
    public void Temperature_from_thermal_zone_is_classified(double celsius, HealthStatus expected)
    {
        var reading = new TemperatureReading("ACPI", [new TemperatureZone("TZ01", celsius, false)], null);

        var result = _analyzer.Analyze(Snapshot(temperature: reading));

        Assert.Equal(expected, result.StatusOf(HealthComponent.Temperature));
    }

    [Fact]
    public void Reported_thermal_throttling_is_at_least_warning()
    {
        var reading = new TemperatureReading("ACPI", [new TemperatureZone("TZ01", 50, PassiveThrottling: true)], null);

        var result = _analyzer.Analyze(Snapshot(temperature: reading));

        Assert.Equal(HealthStatus.Warning, result.StatusOf(HealthComponent.Temperature));
        Assert.Contains(result.Messages, m => m.Text.Contains("throttling", StringComparison.Ordinal));
    }

    [Fact]
    public void Overall_is_healthy_when_everything_is_ok()
    {
        var result = _analyzer.Analyze(Snapshot());

        Assert.Equal(OverallHealth.Healthy, result.Overall);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void Overall_is_attention_when_a_component_is_warning()
    {
        var result = _analyzer.Analyze(Snapshot(memory: Memory(83)));

        Assert.Equal(OverallHealth.Attention, result.Overall);
        var reason = Assert.Single(result.Reasons);
        Assert.Equal(HealthComponent.Memory, reason.Component);
        Assert.Contains("above the warning threshold of 80%", reason.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Overall_is_critical_when_any_component_is_critical()
    {
        var result = _analyzer.Analyze(Snapshot(cpu: Cpu(50), memory: Memory(83), storage: Drives(("C:", 95))));

        Assert.Equal(OverallHealth.Critical, result.Overall);
        Assert.Equal(MessageSeverity.Critical, result.Reasons.First().Severity);
    }

    [Fact]
    public void Several_unknown_metrics_mark_the_diagnosis_incomplete_but_not_critical()
    {
        var snapshot = new HealthSnapshot(null, null, Memory(40), null, null, null, []);

        var result = _analyzer.Analyze(snapshot);

        Assert.Equal(HealthStatus.Unknown, result.StatusOf(HealthComponent.Cpu));
        Assert.Equal(HealthStatus.Unknown, result.StatusOf(HealthComponent.Storage));
        Assert.True(result.IsIncomplete);
        Assert.Equal(OverallHealth.Healthy, result.Overall);
        Assert.Contains(result.Messages, m => m.Text.StartsWith("Diagnosis based on incomplete data", StringComparison.Ordinal));
    }

    [Fact]
    public void No_metric_at_all_is_inconclusive_attention_not_critical()
    {
        var result = _analyzer.Analyze(HealthSnapshot.Empty);

        Assert.Equal(OverallHealth.Attention, result.Overall);
        Assert.All(result.Components, c => Assert.Equal(HealthStatus.Unknown, c.Status));
        Assert.Contains(result.Messages, m => m.Text.Contains("inconclusive", StringComparison.Ordinal));
    }

    [Fact]
    public void Cpu_peak_above_critical_is_reported_as_evidence_without_changing_status()
    {
        var result = _analyzer.Analyze(Snapshot(cpu: Cpu(37, peak: 94)));

        Assert.Equal(HealthStatus.Ok, result.StatusOf(HealthComponent.Cpu));
        Assert.Contains(result.Messages, m => m.Severity == MessageSeverity.Info && m.Text.Contains("peak of 94.0%", StringComparison.Ordinal));
    }

    [Fact]
    public void Messages_never_claim_hardware_defects_or_causes()
    {
        var reading = new TemperatureReading("ACPI", [new TemperatureZone("TZ01", 99, true)], null);
        var result = _analyzer.Analyze(Snapshot(cpu: Cpu(100, 100), memory: Memory(99), storage: Drives(("C:", 99)), temperature: reading));

        string[] forbidden = ["defect", "defeit", "failing", "overheat", "superaquec", "replace", "trocar", "broken"];
        foreach (var message in result.Messages)
        {
            Assert.DoesNotContain(forbidden, word => message.Text.Contains(word, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Collection_issues_become_info_messages()
    {
        var snapshot = Snapshot() with { Issues = [new CollectionIssue(HealthComponent.Cpu, "CPU usage could not be collected.")] };

        var result = _analyzer.Analyze(snapshot);

        Assert.Contains(result.Messages, m => m.Severity == MessageSeverity.Info && m.Text == "CPU usage could not be collected.");
    }

    [Fact]
    public void Long_uptime_is_informational_only()
    {
        var system = FakeSystemInfoCollector.Default with { Uptime = TimeSpan.FromDays(45) };
        var result = _analyzer.Analyze(Snapshot() with { System = system });

        Assert.Equal(OverallHealth.Healthy, result.Overall);
        Assert.Contains(result.Messages, m => m.Severity == MessageSeverity.Info && m.Text.Contains("45 days", StringComparison.Ordinal));
    }

    [Fact]
    public void Custom_thresholds_are_respected()
    {
        var analyzer = new HealthAnalyzer(new ThresholdOptions { CpuWarning = 50, CpuCritical = 60 });

        Assert.Equal(HealthStatus.Warning, analyzer.Analyze(Snapshot(cpu: Cpu(55))).StatusOf(HealthComponent.Cpu));
        Assert.Equal(HealthStatus.Critical, analyzer.Analyze(Snapshot(cpu: Cpu(60))).StatusOf(HealthComponent.Cpu));
    }
}
