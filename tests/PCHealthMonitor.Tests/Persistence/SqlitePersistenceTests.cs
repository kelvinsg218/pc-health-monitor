using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PCHealthMonitor.Application.History;
using PCHealthMonitor.Domain.Health;
using PCHealthMonitor.Infrastructure.Persistence;
using PCHealthMonitor.Tests.Configuration;

namespace PCHealthMonitor.Tests.Persistence;

public class SqlitePersistenceTests
{
    [Fact]
    public void Migrations_create_the_expected_tables()
    {
        using var database = new SqliteTestDatabase();
        using var db = database.CreateContext();

        Assert.Empty(db.Database.GetPendingMigrations());
        Assert.Contains("20260927131042_InitialCreate", db.Database.GetAppliedMigrations());

        var connection = db.Database.GetDbConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
        var tables = new List<string>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        Assert.Contains("Scans", tables);
        Assert.Contains("ScanDrives", tables);
        Assert.Contains("ScanMessages", tables);
    }

    [Fact]
    public async Task Scan_is_persisted_with_drives_and_messages_and_read_back()
    {
        using var database = new SqliteTestDatabase();
        var repository = database.CreateRepository();
        var record = TestData.Record(memoryPercent: 84, diskPercent: 88, TestData.BaseTime);

        var id = await repository.AddAsync(record, CancellationToken.None);
        var loaded = await repository.GetAsync(id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(id, loaded.Id);
        Assert.Equal("TEST-PC", loaded.Hostname);
        Assert.Equal(TestData.BaseTime, loaded.StartedAt);
        Assert.Equal(TimeSpan.Zero, loaded.StartedAt.Offset);
        Assert.Equal(28, loaded.CpuAverage);
        Assert.Equal(61, loaded.CpuPeak);
        Assert.Equal(84, loaded.MemoryUsagePercent!.Value, precision: 3);
        Assert.Equal(HealthStatus.Warning, loaded.MemoryStatus);
        Assert.Equal(HealthStatus.Warning, loaded.StorageStatus);
        Assert.Equal(HealthStatus.Unknown, loaded.TemperatureStatus);
        Assert.Null(loaded.TemperatureCelsius);
        Assert.Equal(OverallHealth.Attention, loaded.OverallHealth);
        Assert.Equal(2, loaded.Drives.Count);
        Assert.Equal(88, loaded.Drives.Single(d => d.Name == "C:").UsagePercent, precision: 3);
        Assert.Equal(record.Messages.Select(m => m.Text), loaded.Messages.Select(m => m.Text));
        Assert.Equal((long)TimeSpan.FromDays(3.5).TotalSeconds, loaded.UptimeSeconds);
    }

    [Fact]
    public async Task Drives_and_messages_are_related_to_their_scan()
    {
        using var database = new SqliteTestDatabase();
        var repository = database.CreateRepository();
        var first = await repository.AddAsync(TestData.Record(70, 70, TestData.BaseTime), CancellationToken.None);
        await repository.AddAsync(TestData.Record(90, 95, TestData.BaseTime.AddDays(1)), CancellationToken.None);

        using var db = database.CreateContext();
        var scan = await db.Scans.Include(s => s.Drives).Include(s => s.Messages).SingleAsync(s => s.Id == first);

        Assert.Equal(2, scan.Drives.Count);
        Assert.All(scan.Drives, d => Assert.Equal(first, d.ScanId));
        Assert.All(scan.Messages, m => Assert.Equal(first, m.ScanId));
        Assert.Equal(4, await db.ScanDrives.CountAsync());
    }

    [Fact]
    public async Task List_returns_most_recent_first_and_respects_limit()
    {
        using var database = new SqliteTestDatabase();
        var repository = database.CreateRepository();

        // Inserted out of chronological order on purpose.
        await repository.AddAsync(TestData.Record(60, 70, TestData.BaseTime.AddDays(5)), CancellationToken.None);
        await repository.AddAsync(TestData.Record(50, 70, TestData.BaseTime), CancellationToken.None);
        await repository.AddAsync(TestData.Record(70, 70, TestData.BaseTime.AddDays(7)), CancellationToken.None);

        var all = await repository.ListRecentAsync(10, CancellationToken.None);
        var two = await repository.ListRecentAsync(2, CancellationToken.None);

        Assert.Equal([TestData.BaseTime.AddDays(7), TestData.BaseTime.AddDays(5), TestData.BaseTime], all.Select(s => s.StartedAt));
        Assert.Equal(2, two.Count);
        Assert.Equal(TestData.BaseTime.AddDays(7), two[0].StartedAt);
    }

    [Fact]
    public async Task Previous_scan_is_the_latest_earlier_scan_of_the_same_host()
    {
        using var database = new SqliteTestDatabase();
        var repository = database.CreateRepository();
        var a = await repository.AddAsync(TestData.Record(60, 70, TestData.BaseTime), CancellationToken.None);
        var b = await repository.AddAsync(TestData.Record(65, 71, TestData.BaseTime.AddDays(1)), CancellationToken.None);
        await repository.AddAsync(TestData.Record(99, 99, TestData.BaseTime.AddDays(1.5), hostname: "OTHER-PC"), CancellationToken.None);
        var c = await repository.AddAsync(TestData.Record(70, 72, TestData.BaseTime.AddDays(2)), CancellationToken.None);

        Assert.Equal(b, (await repository.GetPreviousAsync("TEST-PC", c, CancellationToken.None))!.Id);
        Assert.Equal(a, (await repository.GetPreviousAsync("TEST-PC", b, CancellationToken.None))!.Id);
        Assert.Null(await repository.GetPreviousAsync("TEST-PC", a, CancellationToken.None));
    }

    [Fact]
    public async Task Missing_scan_returns_null()
    {
        using var database = new SqliteTestDatabase();

        Assert.Null(await database.CreateRepository().GetAsync(12345, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_before_cutoff_cascades_to_drives_and_messages()
    {
        using var database = new SqliteTestDatabase();
        var repository = database.CreateRepository();
        await repository.AddAsync(TestData.Record(60, 70, TestData.BaseTime), CancellationToken.None);
        var recent = await repository.AddAsync(TestData.Record(60, 70, TestData.BaseTime.AddDays(10)), CancellationToken.None);

        var deleted = await repository.DeleteStartedBeforeAsync(TestData.BaseTime.AddDays(5), CancellationToken.None);

        Assert.Equal(1, deleted);
        using var db = database.CreateContext();
        Assert.Equal([recent], await db.Scans.Select(s => s.Id).ToListAsync());
        Assert.All(await db.ScanDrives.ToListAsync(), d => Assert.Equal(recent, d.ScanId));
        Assert.All(await db.ScanMessages.ToListAsync(), m => Assert.Equal(recent, m.ScanId));
    }

    [Fact]
    public void Database_file_and_directory_are_created_on_first_use()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "nested", "data", "pchealth.db");
        var database = new PcHealthDatabase(path, NullLogger<PcHealthDatabase>.Instance);

        database.EnsureInitialized();
        database.EnsureInitialized(); // idempotent

        Assert.True(File.Exists(path));
        using var db = database.CreateContext();
        Assert.Empty(db.Database.GetPendingMigrations());
    }

    [Fact]
    public void Unusable_database_path_raises_a_friendly_persistence_error()
    {
        using var dir = new TempDirectory();
        var blocker = Path.Combine(dir.Path, "data");
        File.WriteAllText(blocker, "not a directory"); // a file where the directory should be
        var database = new PcHealthDatabase(Path.Combine(blocker, "pchealth.db"), NullLogger<PcHealthDatabase>.Instance);

        var ex = Assert.Throws<PersistenceException>(database.EnsureInitialized);
        Assert.Contains("could not be opened or created", ex.Message, StringComparison.Ordinal);
    }
}
