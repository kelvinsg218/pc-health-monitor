using Microsoft.EntityFrameworkCore;

namespace PCHealthMonitor.Infrastructure.Persistence;

public sealed class PcHealthDbContext(DbContextOptions<PcHealthDbContext> options) : DbContext(options)
{
    public DbSet<ScanEntity> Scans => Set<ScanEntity>();

    public DbSet<ScanDriveEntity> ScanDrives => Set<ScanDriveEntity>();

    public DbSet<ScanMessageEntity> ScanMessages => Set<ScanMessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScanEntity>(scan =>
        {
            scan.ToTable("Scans");
            scan.HasKey(s => s.Id);
            scan.Property(s => s.Hostname).IsRequired().HasMaxLength(255);
            scan.Property(s => s.WindowsVersion).HasMaxLength(200);
            scan.Property(s => s.CpuStatus).IsRequired().HasMaxLength(16);
            scan.Property(s => s.MemoryStatus).IsRequired().HasMaxLength(16);
            scan.Property(s => s.StorageStatus).IsRequired().HasMaxLength(16);
            scan.Property(s => s.TemperatureStatus).IsRequired().HasMaxLength(16);
            scan.Property(s => s.OverallHealth).IsRequired().HasMaxLength(16);
            scan.Property(s => s.StartedAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            scan.Property(s => s.FinishedAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            scan.HasIndex(s => s.StartedAtUtc);
            scan.HasIndex(s => new { s.Hostname, s.StartedAtUtc });

            scan.HasMany(s => s.Drives).WithOne(d => d.Scan).HasForeignKey(d => d.ScanId).OnDelete(DeleteBehavior.Cascade);
            scan.HasMany(s => s.Messages).WithOne(m => m.Scan).HasForeignKey(m => m.ScanId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScanDriveEntity>(drive =>
        {
            drive.ToTable("ScanDrives");
            drive.HasKey(d => d.Id);
            drive.Property(d => d.Name).IsRequired().HasMaxLength(64);
            drive.Property(d => d.FileSystem).HasMaxLength(32);
            drive.Property(d => d.Status).IsRequired().HasMaxLength(16);
        });

        modelBuilder.Entity<ScanMessageEntity>(message =>
        {
            message.ToTable("ScanMessages");
            message.HasKey(m => m.Id);
            message.Property(m => m.Severity).IsRequired().HasMaxLength(16);
            message.Property(m => m.Component).IsRequired().HasMaxLength(32);
            message.Property(m => m.Text).IsRequired().HasMaxLength(1000);
        });
    }
}

/// <summary>One persisted full scan (monitor refreshes are never persisted).</summary>
public sealed class ScanEntity
{
    public int Id { get; set; }

    public string Hostname { get; set; } = string.Empty;

    public DateTime StartedAtUtc { get; set; }

    public DateTime FinishedAtUtc { get; set; }

    public string? WindowsVersion { get; set; }

    public double? CpuAverage { get; set; }

    public double? CpuPeak { get; set; }

    public double? CpuMinimum { get; set; }

    public int? CpuSampleCount { get; set; }

    public double? MemoryUsagePercent { get; set; }

    public long? MemoryTotalBytes { get; set; }

    public long? MemoryUsedBytes { get; set; }

    public double? TemperatureCelsius { get; set; }

    public long? UptimeSeconds { get; set; }

    public string CpuStatus { get; set; } = string.Empty;

    public string MemoryStatus { get; set; } = string.Empty;

    public string StorageStatus { get; set; } = string.Empty;

    public string TemperatureStatus { get; set; } = string.Empty;

    public string OverallHealth { get; set; } = string.Empty;

    public bool IsIncomplete { get; set; }

    public List<ScanDriveEntity> Drives { get; set; } = [];

    public List<ScanMessageEntity> Messages { get; set; } = [];
}

public sealed class ScanDriveEntity
{
    public int Id { get; set; }

    public int ScanId { get; set; }

    public ScanEntity? Scan { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? FileSystem { get; set; }

    public long TotalBytes { get; set; }

    public long FreeBytes { get; set; }

    public double UsagePercent { get; set; }

    public string Status { get; set; } = string.Empty;
}

public sealed class ScanMessageEntity
{
    public int Id { get; set; }

    public int ScanId { get; set; }

    public ScanEntity? Scan { get; set; }

    /// <summary>Keeps the original message order.</summary>
    public int Position { get; set; }

    public string Severity { get; set; } = string.Empty;

    public string Component { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}
