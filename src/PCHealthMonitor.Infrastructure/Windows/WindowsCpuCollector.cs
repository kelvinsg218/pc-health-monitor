using System.ComponentModel;
using Microsoft.Win32;
using PCHealthMonitor.Application.Abstractions;
using PCHealthMonitor.Application.Sampling;
using PCHealthMonitor.Domain.Metrics;

namespace PCHealthMonitor.Infrastructure.Windows;

/// <summary>
/// Total CPU usage from GetSystemTimes deltas (time-based "% processor time").
/// Task Manager on Windows 10 1903+/11 shows "% Processor Utility", which also accounts for
/// clock frequency (turbo/power saving), so values can differ from Task Manager.
/// </summary>
public sealed class WindowsCpuCollector : ICpuCollector
{
    private readonly Lazy<CpuInfo> _info = new(ReadInfo);

    public CpuInfo GetInfo() => _info.Value;

    public async Task<double> MeasureUsageAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        var before = ReadTimes();
        await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        var after = ReadTimes();
        return CpuUsageCalculator.Calculate(before, after);
    }

    internal static CpuTimes ReadTimes()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            throw new Win32Exception();
        }

        return new CpuTimes(idle, kernel, user);
    }

    private static CpuInfo ReadInfo()
    {
        string? name = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            name = (key?.GetValue("ProcessorNameString") as string)?.Trim();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Name stays unknown; usage can still be measured.
        }

        return new CpuInfo(string.IsNullOrWhiteSpace(name) ? null : name, Environment.ProcessorCount);
    }
}
