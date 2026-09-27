using Microsoft.Extensions.Logging;
using PCHealthMonitor.Application.Abstractions;
using PCHealthMonitor.Domain.Metrics;

namespace PCHealthMonitor.Infrastructure.Windows;

/// <summary>
/// Local fixed drives (with a drive letter) via DriveInfo. Optical, removable, network and RAM
/// drives are ignored so that an empty DVD drive or a disconnected share is never reported as a
/// health problem. A fixed drive that is not ready (e.g. locked by BitLocker) is reported as unreadable.
/// "Free" is TotalFreeSpace (the same value Explorer shows), not the per-user quota.
/// </summary>
public sealed class DriveStorageCollector(ILogger<DriveStorageCollector> logger) : IStorageCollector
{
    public StorageSnapshot Collect()
    {
        var drives = new List<DriveMetrics>();
        var unreadable = new List<string>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            var name = drive.Name.TrimEnd('\\');
            if (drive.DriveType != DriveType.Fixed)
            {
                continue;
            }

            try
            {
                if (!drive.IsReady)
                {
                    unreadable.Add(name);
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? null : drive.VolumeLabel;
                drives.Add(new DriveMetrics(name, drive.DriveFormat, label, (ulong)drive.TotalSize, (ulong)drive.TotalFreeSpace));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                logger.LogWarning(ex, "Drive {Drive} could not be read.", name);
                unreadable.Add(name);
            }
        }

        return new StorageSnapshot(drives, unreadable);
    }
}
