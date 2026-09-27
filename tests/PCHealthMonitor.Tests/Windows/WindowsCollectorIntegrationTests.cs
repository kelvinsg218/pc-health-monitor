using Microsoft.Extensions.Logging.Abstractions;
using PCHealthMonitor.Infrastructure.Windows;

namespace PCHealthMonitor.Tests.Windows;

/// <summary>
/// Read-only sanity checks of the real Windows collectors. They only assert invariants that hold on
/// any Windows machine (no exact values). Exclude them with:
/// dotnet test --filter "Category!=WindowsIntegration"
/// </summary>
[Trait("Category", "WindowsIntegration")]
public class WindowsCollectorIntegrationTests
{
    [Fact]
    public async Task Cpu_collector_returns_a_percentage()
    {
        var collector = new WindowsCpuCollector();

        var usage = await collector.MeasureUsageAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None);

        Assert.InRange(usage, 0, 100);
        Assert.Equal(Environment.ProcessorCount, collector.GetInfo().LogicalProcessorCount);
    }

    [Fact]
    public void Memory_collector_returns_consistent_values()
    {
        var memory = new WindowsMemoryCollector().Collect();

        Assert.True(memory.TotalBytes > 0);
        Assert.True(memory.AvailableBytes <= memory.TotalBytes);
        Assert.InRange(memory.UsagePercent, 0, 100);
        if (memory.InstalledBytes is { } installed)
        {
            Assert.True(installed >= memory.TotalBytes);
        }
    }

    [Fact]
    public void Storage_collector_returns_only_fixed_drives_with_consistent_values()
    {
        var storage = new DriveStorageCollector(NullLogger<DriveStorageCollector>.Instance).Collect();

        Assert.NotEmpty(storage.Drives);
        Assert.All(storage.Drives, d =>
        {
            Assert.True(d.TotalBytes > 0);
            Assert.True(d.FreeBytes <= d.TotalBytes);
            Assert.Equal(DriveType.Fixed, new DriveInfo(d.Name).DriveType);
        });
    }

    [Fact]
    public void Process_collector_lists_processes_with_cpu_times_without_elevation()
    {
        var snapshot = new WindowsProcessCollector(NullLogger<WindowsProcessCollector>.Instance).Capture();

        Assert.True(snapshot.Processes.Count > 10);
        Assert.Contains(snapshot.Processes, p => p.Pid == Environment.ProcessId);
        Assert.All(snapshot.Processes, p => Assert.False(string.IsNullOrEmpty(p.Name)));

        // The system process list exposes CPU times for (nearly) every process, even protected ones.
        var withCpu = snapshot.Processes.Count(p => p.TotalProcessorTime is not null);
        Assert.True(withCpu >= snapshot.Processes.Count * 0.9, $"Only {withCpu} of {snapshot.Processes.Count} processes had CPU times.");

        var self = snapshot.Processes.Single(p => p.Pid == Environment.ProcessId);
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        Assert.Equal(current.ProcessName, self.Name, ignoreCase: true);
    }

    [Fact]
    public void System_info_collector_reports_windows_and_uptime()
    {
        var info = new WindowsSystemInfoCollector(TimeProvider.System).Collect();

        Assert.Equal(Environment.MachineName, info.Hostname);
        Assert.Contains(info.WindowsName, new[] { "Windows 10", "Windows 11" });
        Assert.NotNull(info.Build);
        Assert.True(info.Uptime > TimeSpan.Zero);
    }

    [Fact]
    public void Temperature_provider_never_throws_and_never_returns_implausible_values()
    {
        var reading = new ThermalZoneTemperatureProvider(NullLogger<ThermalZoneTemperatureProvider>.Instance).Read();

        if (reading.IsAvailable)
        {
            Assert.All(reading.Zones, z => Assert.InRange(z.Celsius, 1, 125));
        }
        else
        {
            Assert.NotNull(reading.UnavailableReason);
        }
    }
}
