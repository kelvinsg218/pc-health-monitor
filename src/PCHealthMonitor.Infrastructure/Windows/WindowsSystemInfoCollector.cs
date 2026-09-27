using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PCHealthMonitor.Application.Abstractions;
using PCHealthMonitor.Domain.Machine;

namespace PCHealthMonitor.Infrastructure.Windows;

/// <summary>
/// Hostname, Windows edition/version/build (read-only registry query), architecture, uptime and user.
/// The Windows name is derived from the build number because "ProductName" says "Windows 10" on Windows 11.
/// Uptime is GetTickCount64 (time since the kernel booted, including sleep). With Fast Startup enabled,
/// "Shut down" does not restart the kernel, so uptime keeps growing until a real restart.
/// </summary>
public sealed class WindowsSystemInfoCollector(TimeProvider timeProvider) : ISystemInfoCollector
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    public SystemInfo Collect()
    {
        string? productName = null, editionId = null, displayVersion = null, registryBuild = null;
        int? ubr = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(CurrentVersionKey);
            if (key is not null)
            {
                productName = key.GetValue("ProductName") as string;
                editionId = key.GetValue("EditionID") as string;
                displayVersion = key.GetValue("DisplayVersion") as string ?? key.GetValue("ReleaseId") as string;
                registryBuild = key.GetValue("CurrentBuildNumber") as string ?? key.GetValue("CurrentBuild") as string;
                ubr = key.GetValue("UBR") as int?;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Leave the values unknown.
        }

        // Environment.OSVersion is not affected by compatibility shims in .NET 5+.
        int? build = Environment.OSVersion.Version.Build > 0
            ? Environment.OSVersion.Version.Build
            : int.TryParse(registryBuild, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

        var buildText = build is null ? null : ubr is null ? build.Value.ToString(CultureInfo.InvariantCulture) : $"{build}.{ubr}";

        return new SystemInfo(
            Environment.MachineName,
            WindowsVersionResolver.ResolveName(build, productName),
            WindowsVersionResolver.ResolveEdition(productName, editionId),
            string.IsNullOrWhiteSpace(displayVersion) ? null : displayVersion,
            buildText,
            DescribeArchitecture(RuntimeInformation.OSArchitecture),
            GetUptime(),
            Environment.UserName,
            timeProvider.GetLocalNow());
    }

    public TimeSpan? GetUptime() => TimeSpan.FromMilliseconds(Environment.TickCount64);

    private static string DescribeArchitecture(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "x64",
        Architecture.X86 => "x86",
        Architecture.Arm64 => "ARM64",
        Architecture.Arm => "ARM",
        _ => architecture.ToString(),
    };
}
