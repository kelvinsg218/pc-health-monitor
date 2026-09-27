using Microsoft.EntityFrameworkCore;
using PCHealthMonitor.Application.History;
using PCHealthMonitor.Domain.Health;

namespace PCHealthMonitor.Infrastructure.Persistence;

/// <summary>EF Core/SQLite scan history. A short-lived DbContext is created per operation.</summary>
public sealed class SqliteScanRepository(Func<PcHealthDbContext> contextFactory) : IScanRepository
{
    public async Task<int> AddAsync(ScanRecord scan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scan);

        await using var db = contextFactory();
        var entity = ToEntity(scan);
        db.Scans.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entity.Id;
    }

    public async Task<IReadOnlyList<ScanRecord>> ListRecentAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = contextFactory();
        var entities = await db.Scans.AsNoTracking()
            .OrderByDescending(s => s.StartedAtUtc)
            .ThenByDescending(s => s.Id)
            .Take(limit)
            .Include(s => s.Drives)
            .AsSplitQuery()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return entities.Select(ToRecord).ToList();
    }

    public async Task<ScanRecord?> GetAsync(int id, CancellationToken cancellationToken)
    {
        await using var db = contextFactory();
        var entity = await db.Scans.AsNoTracking()
            .Include(s => s.Drives)
            .Include(s => s.Messages)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : ToRecord(entity);
    }

    public async Task<ScanRecord?> GetPreviousAsync(string hostname, int scanId, CancellationToken cancellationToken)
    {
        await using var db = contextFactory();
        var current = await db.Scans.AsNoTracking()
            .Where(s => s.Id == scanId)
            .Select(s => new { s.StartedAtUtc })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return null;
        }

        var previous = await db.Scans.AsNoTracking()
            .Where(s => s.Hostname == hostname && s.Id != scanId
                && (s.StartedAtUtc < current.StartedAtUtc || (s.StartedAtUtc == current.StartedAtUtc && s.Id < scanId)))
            .OrderByDescending(s => s.StartedAtUtc)
            .ThenByDescending(s => s.Id)
            .Include(s => s.Drives)
            .AsSplitQuery()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return previous is null ? null : ToRecord(previous);
    }

    public async Task<int> DeleteStartedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        await using var db = contextFactory();
        var cutoffUtc = cutoff.UtcDateTime;

        // Load then remove so that the cascade to drives/messages is applied by EF as well as SQLite.
        var old = await db.Scans.Where(s => s.StartedAtUtc < cutoffUtc)
            .Include(s => s.Drives)
            .Include(s => s.Messages)
            .AsSplitQuery()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (old.Count == 0)
        {
            return 0;
        }

        db.Scans.RemoveRange(old);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return old.Count;
    }

    internal static ScanEntity ToEntity(ScanRecord scan) => new()
    {
        Hostname = scan.Hostname,
        StartedAtUtc = scan.StartedAt.UtcDateTime,
        FinishedAtUtc = scan.FinishedAt.UtcDateTime,
        WindowsVersion = Truncate(scan.WindowsVersion, 200),
        CpuAverage = scan.CpuAverage,
        CpuPeak = scan.CpuPeak,
        CpuMinimum = scan.CpuMinimum,
        CpuSampleCount = scan.CpuSampleCount,
        MemoryUsagePercent = scan.MemoryUsagePercent,
        MemoryTotalBytes = scan.MemoryTotalBytes,
        MemoryUsedBytes = scan.MemoryUsedBytes,
        TemperatureCelsius = scan.TemperatureCelsius,
        UptimeSeconds = scan.UptimeSeconds,
        CpuStatus = scan.CpuStatus.ToLabel(),
        MemoryStatus = scan.MemoryStatus.ToLabel(),
        StorageStatus = scan.StorageStatus.ToLabel(),
        TemperatureStatus = scan.TemperatureStatus.ToLabel(),
        OverallHealth = scan.OverallHealth.ToLabel(),
        IsIncomplete = scan.IsIncomplete,
        Drives = scan.Drives.Select(d => new ScanDriveEntity
        {
            Name = d.Name,
            FileSystem = d.FileSystem,
            TotalBytes = d.TotalBytes,
            FreeBytes = d.FreeBytes,
            UsagePercent = double.IsFinite(d.UsagePercent) ? d.UsagePercent : 0,
            Status = d.Status.ToLabel(),
        }).ToList(),
        Messages = scan.Messages.Select((m, index) => new ScanMessageEntity
        {
            Position = index,
            Severity = m.Severity.ToLabel(),
            Component = m.Component.ToLabel(),
            Text = Truncate(m.Text, 1000)!,
        }).ToList(),
    };

    internal static ScanRecord ToRecord(ScanEntity entity) => new()
    {
        Id = entity.Id,
        Hostname = entity.Hostname,
        StartedAt = new DateTimeOffset(DateTime.SpecifyKind(entity.StartedAtUtc, DateTimeKind.Utc)),
        FinishedAt = new DateTimeOffset(DateTime.SpecifyKind(entity.FinishedAtUtc, DateTimeKind.Utc)),
        WindowsVersion = entity.WindowsVersion,
        CpuAverage = entity.CpuAverage,
        CpuPeak = entity.CpuPeak,
        CpuMinimum = entity.CpuMinimum,
        CpuSampleCount = entity.CpuSampleCount,
        MemoryUsagePercent = entity.MemoryUsagePercent,
        MemoryTotalBytes = entity.MemoryTotalBytes,
        MemoryUsedBytes = entity.MemoryUsedBytes,
        TemperatureCelsius = entity.TemperatureCelsius,
        UptimeSeconds = entity.UptimeSeconds,
        CpuStatus = HealthLabels.ParseStatus(entity.CpuStatus),
        MemoryStatus = HealthLabels.ParseStatus(entity.MemoryStatus),
        StorageStatus = HealthLabels.ParseStatus(entity.StorageStatus),
        TemperatureStatus = HealthLabels.ParseStatus(entity.TemperatureStatus),
        OverallHealth = HealthLabels.ParseOverall(entity.OverallHealth) ?? OverallHealth.Attention,
        IsIncomplete = entity.IsIncomplete,
        Drives = entity.Drives
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => new ScanDriveRecord(d.Name, d.FileSystem, d.TotalBytes, d.FreeBytes, d.UsagePercent, HealthLabels.ParseStatus(d.Status)))
            .ToList(),
        Messages = entity.Messages
            .OrderBy(m => m.Position)
            .Select(m => new DiagnosticMessage(HealthLabels.ParseSeverity(m.Severity), HealthLabels.ParseComponent(m.Component), m.Text))
            .ToList(),
    };

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
