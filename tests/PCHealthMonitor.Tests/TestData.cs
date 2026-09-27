using PCHealthMonitor.Application.Analysis;
using PCHealthMonitor.Application.Configuration;
using PCHealthMonitor.Application.History;
using PCHealthMonitor.Application.Scanning;
using PCHealthMonitor.Domain.Health;
using PCHealthMonitor.Domain.Metrics;
using PCHealthMonitor.Tests.Fakes;

namespace PCHealthMonitor.Tests;

public static class TestData
{
    public const ulong Gb = 1024UL * 1024 * 1024;

    public static readonly DateTimeOffset BaseTime = new(2026, 9, 20, 9, 30, 0, TimeSpan.Zero);

    public static HealthCheckResult Result(
        double cpuAverage = 28,
        double cpuPeak = 61,
        double memoryPercent = 72,
        double diskPercent = 74,
        DateTimeOffset? startedAt = null,
        string hostname = "TEST-PC",
        TemperatureReading? temperature = null,
        ProcessSummary? processes = null)
    {
        var start = startedAt ?? BaseTime;
        var system = FakeSystemInfoCollector.Default with { Hostname = hostname, Timestamp = start };
        var snapshot = new HealthSnapshot(
            system,
            new CpuMetrics(new CpuInfo("Fake CPU", 8), new SampleStatistics(cpuAverage / 2, cpuPeak, cpuAverage, 5), TimeSpan.FromSeconds(1)),
            new MemoryMetrics(16 * Gb, (ulong)(16 * Gb * (100 - memoryPercent) / 100.0), 16 * Gb),
            new StorageSnapshot(
                [
                    new DriveMetrics("C:", "NTFS", "System", 500 * Gb, (ulong)(500 * Gb * (100 - diskPercent) / 100.0)),
                    new DriveMetrics("D:", "NTFS", "Data", 1000 * Gb, 800 * Gb),
                ],
                []),
            temperature ?? TemperatureReading.Unavailable("Windows does not expose any thermal zone on this computer."),
            processes,
            []);
        var assessment = new HealthAnalyzer(new ThresholdOptions()).Analyze(snapshot);
        return new HealthCheckResult(start, start.AddSeconds(6), hostname, snapshot, assessment, IsFullScan: true);
    }

    public static ScanRecord Record(double memoryPercent, double diskPercent, DateTimeOffset startedAt, string hostname = "TEST-PC") =>
        ScanRecord.FromResult(Result(memoryPercent: memoryPercent, diskPercent: diskPercent, startedAt: startedAt, hostname: hostname));

    public static ProcessSummary Processes() => new(
        [new ProcessUsage(100, "chrome", 900 * 1024 * 1024, 3.5), new ProcessUsage(200, "Code", 400 * 1024 * 1024, 12.25)],
        [new ProcessUsage(200, "Code", 400 * 1024 * 1024, 12.25), new ProcessUsage(100, "chrome", 900 * 1024 * 1024, 3.5)],
        120,
        3,
        0,
        TimeSpan.FromSeconds(5));

    public static HealthStatus StatusOf(HealthCheckResult result, HealthComponent component) => result.Assessment.StatusOf(component);
}
