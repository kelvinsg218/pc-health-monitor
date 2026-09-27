using System.ComponentModel;
using PCHealthMonitor.Application.Abstractions;
using PCHealthMonitor.Domain.Metrics;

namespace PCHealthMonitor.Infrastructure.Windows;

/// <summary>Physical memory from GlobalMemoryStatusEx; installed RAM from GetPhysicallyInstalledSystemMemory.</summary>
public sealed class WindowsMemoryCollector : IMemoryCollector
{
    public MemoryMetrics Collect()
    {
        var status = NativeMethods.MemoryStatusEx.Create();
        if (!NativeMethods.GlobalMemoryStatusEx(ref status))
        {
            throw new Win32Exception();
        }

        ulong? installed = NativeMethods.GetPhysicallyInstalledSystemMemory(out var kilobytes) && kilobytes > 0
            ? kilobytes * 1024
            : null;

        return new MemoryMetrics(status.TotalPhys, status.AvailPhys, installed);
    }
}
