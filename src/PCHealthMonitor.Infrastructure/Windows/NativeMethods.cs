using System.Runtime.InteropServices;

namespace PCHealthMonitor.Infrastructure.Windows;

/// <summary>Read-only kernel32 queries used by the collectors.</summary>
internal static partial class NativeMethods
{
    /// <summary>Cumulative idle/kernel/user times for all processors (FILETIME, 100 ns units). Kernel includes idle.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    /// <summary>Installed RAM (from SMBIOS) in kilobytes; can fail on VMs or broken firmware tables.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

    /// <summary>Read-only system query. Used with SystemProcessInformation (class 5) to list processes.</summary>
    [LibraryImport("ntdll.dll")]
    internal static partial uint NtQuerySystemInformation(int systemInformationClass, IntPtr systemInformation, uint systemInformationLength, out uint returnLength);

    /// <summary>
    /// Leading part of SYSTEM_PROCESS_INFORMATION (same layout as .NET's own interop definition).
    /// Only the fields up to WorkingSetSize are declared; entries are larger and are walked via NextEntryOffset.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemProcessInformation
    {
        public uint NextEntryOffset;
        public uint NumberOfThreads;
        public long WorkingSetPrivateSize;
        public uint HardFaultCount;
        public uint NumberOfThreadsHighWatermark;
        public ulong CycleTime;
        public long CreateTime;
        public long UserTime;
        public long KernelTime;
        public UnicodeString ImageName;
        public int BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
        public uint HandleCount;
        public uint SessionId;
        public UIntPtr UniqueProcessKey;
        public UIntPtr PeakVirtualSize;
        public UIntPtr VirtualSize;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;

        public static MemoryStatusEx Create() => new() { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
    }
}
