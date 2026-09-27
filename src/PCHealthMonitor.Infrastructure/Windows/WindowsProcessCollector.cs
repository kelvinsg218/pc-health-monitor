using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PCHealthMonitor.Application.Abstractions;
using PCHealthMonitor.Domain.Metrics;

namespace PCHealthMonitor.Infrastructure.Windows;

/// <summary>
/// Snapshot of running processes: PID, name, working set and cumulative CPU time only
/// (no command lines, window titles or other personal data).
/// <para>
/// Primary source: the system process list (NtQuerySystemInformation / SystemProcessInformation),
/// the same read-only source used by Task Manager and by .NET's Process.GetProcesses(). It includes CPU
/// times for every process without opening process handles, so it works without administrator rights.
/// </para>
/// <para>
/// Fallback: System.Diagnostics.Process. There, CPU time requires opening each process, which protected
/// and other users' processes refuse → CPU is null for those (skipped, never fatal).
/// </para>
/// </summary>
public sealed class WindowsProcessCollector(ILogger<WindowsProcessCollector> logger) : IProcessCollector
{
    private const int SystemProcessInformationClass = 5;
    private const uint StatusInfoLengthMismatch = 0xC0000004;
    private const int MaxBufferBytes = 64 * 1024 * 1024;

    private int _bufferSize = 1024 * 1024;
    private bool _nativeFailed;

    public ProcessSnapshot Capture()
    {
        if (!_nativeFailed)
        {
            try
            {
                return CaptureFromSystemProcessList();
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or EntryPointNotFoundException or DllNotFoundException or OutOfMemoryException)
            {
                _nativeFailed = true;
                logger.LogWarning(ex, "System process list unavailable; falling back to System.Diagnostics.Process (CPU may be unavailable for protected processes).");
            }
        }

        return CaptureWithProcessClass();
    }

    private ProcessSnapshot CaptureFromSystemProcessList()
    {
        while (true)
        {
            var buffer = Marshal.AllocHGlobal(_bufferSize);
            try
            {
                var timestamp = Stopwatch.GetTimestamp();
                var status = NativeMethods.NtQuerySystemInformation(SystemProcessInformationClass, buffer, (uint)_bufferSize, out var needed);
                if (status == StatusInfoLengthMismatch)
                {
                    var next = Math.Max(_bufferSize * 2, (int)Math.Min(needed + (256 * 1024), int.MaxValue));
                    if (next > MaxBufferBytes)
                    {
                        throw new InvalidOperationException("Process list is too large.");
                    }

                    _bufferSize = next;
                    continue;
                }

                if (status != 0)
                {
                    throw new Win32Exception($"NtQuerySystemInformation failed with NTSTATUS 0x{status:X8}.");
                }

                return new ProcessSnapshot(ToTimeSpan(timestamp), ParseProcessList(buffer));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private static List<ProcessSample> ParseProcessList(IntPtr buffer)
    {
        var samples = new List<ProcessSample>(512);
        var offset = 0L;
        while (true)
        {
            var entry = IntPtr.Add(buffer, checked((int)offset));
            var info = Marshal.PtrToStructure<NativeMethods.SystemProcessInformation>(entry);
            var pid = checked((int)info.UniqueProcessId.ToInt64());

            string name;
            if (info.ImageName.Buffer != IntPtr.Zero && info.ImageName.Length > 0)
            {
                name = Marshal.PtrToStringUni(info.ImageName.Buffer, info.ImageName.Length / 2);
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    name = name[..^4];
                }
            }
            else
            {
                name = pid == 0 ? "Idle" : "Unknown";
            }

            samples.Add(new ProcessSample(
                pid,
                name,
                (long)(ulong)info.WorkingSetSize,
                TimeSpan.FromTicks(info.UserTime + info.KernelTime)));

            if (info.NextEntryOffset == 0)
            {
                break;
            }

            offset += info.NextEntryOffset;
        }

        return samples;
    }

    private static ProcessSnapshot CaptureWithProcessClass()
    {
        var before = Stopwatch.GetTimestamp();
        var processes = Process.GetProcesses();
        var samples = new List<ProcessSample>(processes.Length);
        try
        {
            foreach (var process in processes)
            {
                if (TryRead(process) is { } sample)
                {
                    samples.Add(sample);
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        // Use the middle of the enumeration as the capture time to minimize per-process skew.
        var after = Stopwatch.GetTimestamp();
        return new ProcessSnapshot(ToTimeSpan(before + ((after - before) / 2)), samples);
    }

    private static ProcessSample? TryRead(Process process)
    {
        int pid;
        string name;
        try
        {
            pid = process.Id;
            name = process.ProcessName;
        }
        catch (InvalidOperationException)
        {
            return null; // exited while being read
        }

        long? workingSet = null;
        try
        {
            workingSet = process.WorkingSet64;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }

        TimeSpan? cpu = null;
        if (pid != 0)
        {
            try
            {
                cpu = process.TotalProcessorTime;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException or UnauthorizedAccessException)
            {
                // Access denied (protected process) or process exited: CPU unavailable for this process.
            }
        }

        return new ProcessSample(pid, name, workingSet, cpu);
    }

    private static TimeSpan ToTimeSpan(long stopwatchTimestamp) =>
        TimeSpan.FromSeconds(stopwatchTimestamp / (double)Stopwatch.Frequency);
}
