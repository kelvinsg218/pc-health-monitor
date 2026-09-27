using PCHealthMonitor.Application.Configuration;
using PCHealthMonitor.Application.Formatting;
using PCHealthMonitor.Application.History;
using PCHealthMonitor.Domain.Health;
using PCHealthMonitor.Tests.Fakes;
using PCHealthMonitor.Tests.Persistence;

namespace PCHealthMonitor.Tests.History;

public class HistoryTests
{
    [Fact]
    public void Comparison_reports_absolute_change_in_percentage_points()
    {
        var previous = TestData.Record(memoryPercent: 68, diskPercent: 74, TestData.BaseTime);
        var current = TestData.Record(memoryPercent: 79, diskPercent: 81, TestData.BaseTime.AddDays(5));

        var comparison = HistoryComparer.Compare(previous, current);

        var memory = comparison.Changes.Single(c => c.Metric == "Memory usage");
        Assert.Equal(11, memory.DeltaPoints!.Value, precision: 6);
        var disk = comparison.Changes.Single(c => c.Metric == "Drive C: usage");
        Assert.Equal(7, disk.DeltaPoints!.Value, precision: 6);
        Assert.Equal("+7.0 percentage points", Display.PercentagePoints(disk.DeltaPoints.Value));
    }

    [Fact]
    public void Seventy_to_eighty_percent_is_ten_percentage_points_not_ten_percent()
    {
        var change = new MetricChange("Memory usage", 70, 80);

        Assert.Equal(10, change.DeltaPoints!.Value, precision: 9);
        var text = Display.PercentagePoints(change.DeltaPoints.Value);
        Assert.Equal("+10.0 percentage points", text);
        Assert.DoesNotContain("%", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Decrease_and_no_change_are_formatted()
    {
        Assert.Equal("-3.5 percentage points", Display.PercentagePoints(-3.5));
        Assert.Equal("no change (0.0 percentage points)", Display.PercentagePoints(0.01));
    }

    [Fact]
    public void Drive_present_in_only_one_scan_has_no_delta()
    {
        var previous = TestData.Record(60, 70, TestData.BaseTime) with { Drives = [] };
        var current = TestData.Record(60, 70, TestData.BaseTime.AddDays(1));

        var comparison = HistoryComparer.Compare(previous, current);

        Assert.All(comparison.Changes.Where(c => c.Metric.StartsWith("Drive", StringComparison.Ordinal)), c =>
        {
            Assert.Null(c.Previous);
            Assert.Null(c.DeltaPoints);
        });
    }

    [Fact]
    public void Overall_transition_is_reported()
    {
        var previous = TestData.Record(60, 70, TestData.BaseTime);
        var current = TestData.Record(95, 70, TestData.BaseTime.AddDays(1));

        var comparison = HistoryComparer.Compare(previous, current);

        Assert.True(comparison.OverallChanged);
        Assert.Equal("HEALTHY → CRITICAL", comparison.OverallTransition);
    }

    [Fact]
    public async Task Service_saves_multiple_scans_and_compares_with_previous()
    {
        using var database = new SqliteTestDatabase();
        var clock = new ManualTimeProvider(TestData.BaseTime.AddDays(3));
        var service = new HistoryService(database.CreateRepository(), new HistoryOptions(), clock);

        var first = await service.SaveAsync(TestData.Result(memoryPercent: 68, diskPercent: 74, startedAt: TestData.BaseTime), CancellationToken.None);
        var second = await service.SaveAsync(TestData.Result(memoryPercent: 79, diskPercent: 81, startedAt: TestData.BaseTime.AddDays(2)), CancellationToken.None);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Null(await service.CompareWithPreviousAsync(first, CancellationToken.None));

        var comparison = await service.CompareWithPreviousAsync(second, CancellationToken.None);
        Assert.NotNull(comparison);
        Assert.Equal(first.Id, comparison.Previous.Id);
        Assert.Equal(11, comparison.Changes.Single(c => c.Metric == "Memory usage").DeltaPoints!.Value, precision: 3);

        var list = await service.ListAsync(10, CancellationToken.None);
        Assert.Equal([second.Id, first.Id], list.Select(s => s.Id));
    }

    [Fact]
    public async Task Retention_removes_scans_older_than_configured_days()
    {
        using var database = new SqliteTestDatabase();
        var clock = new ManualTimeProvider(TestData.BaseTime.AddDays(100));
        var service = new HistoryService(database.CreateRepository(), new HistoryOptions { RetentionDays = 30 }, clock);

        await service.SaveAsync(TestData.Result(startedAt: TestData.BaseTime), CancellationToken.None); // 100 days old
        var kept = await service.SaveAsync(TestData.Result(startedAt: TestData.BaseTime.AddDays(99)), CancellationToken.None);

        var list = await service.ListAsync(10, CancellationToken.None);
        Assert.Equal([kept.Id], list.Select(s => s.Id));
    }

    [Fact]
    public async Task Retention_zero_keeps_everything()
    {
        using var database = new SqliteTestDatabase();
        var clock = new ManualTimeProvider(TestData.BaseTime.AddYears(5));
        var service = new HistoryService(database.CreateRepository(), new HistoryOptions { RetentionDays = 0 }, clock);

        await service.SaveAsync(TestData.Result(startedAt: TestData.BaseTime), CancellationToken.None);
        await service.SaveAsync(TestData.Result(startedAt: TestData.BaseTime.AddDays(1)), CancellationToken.None);

        Assert.Equal(2, (await service.ListAsync(10, CancellationToken.None)).Count);
    }

    [Fact]
    public void Record_from_result_keeps_statuses_and_unknown_temperature()
    {
        var record = ScanRecord.FromResult(TestData.Result(memoryPercent: 93));

        Assert.Equal(HealthStatus.Critical, record.MemoryStatus);
        Assert.Equal(OverallHealth.Critical, record.OverallHealth);
        Assert.Equal(HealthStatus.Unknown, record.TemperatureStatus);
        Assert.Null(record.TemperatureCelsius);
        Assert.Equal(2, record.Drives.Count);
    }
}
