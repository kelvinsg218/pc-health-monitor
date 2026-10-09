using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging;

namespace PCHealthMonitor.Infrastructure.Persistence;

/// <summary>Creates the data directory and applies EF Core migrations on first use.</summary>
public sealed class PcHealthDatabase(string databasePath, ILogger<PcHealthDatabase> logger)
{
    private readonly object _initLock = new();
    private bool _initialized;

    public string DatabasePath { get; } = Path.GetFullPath(databasePath);

    public static DbContextOptions<PcHealthDbContext> CreateOptions(string connectionString) =>
        new DbContextOptionsBuilder<PcHealthDbContext>().UseSqlite(connectionString).Options;

    public PcHealthDbContext CreateContext() => new(CreateOptions(ConnectionString));

    public string ConnectionString => new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString();

    /// <summary>Idempotent: ensures the directory exists and the schema is up to date.</summary>
    /// <exception cref="PersistenceException">The database could not be created or migrated.</exception>
    public void EnsureInitialized()
    {
        lock (_initLock)
        {
            EnsureInitializedCore();
        }
    }

    private void EnsureInitializedCore()
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var db = CreateContext();
            var pending = db.Database.GetPendingMigrations().ToList();
            if (pending.Count > 0)
            {
                logger.LogInformation("Applying {Count} database migration(s) to {Path}: {Migrations}", pending.Count, DatabasePath, string.Join(", ", pending));
                db.Database.Migrate();
            }

            _initialized = true;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(ex, "Database initialization failed for {Path}.", DatabasePath);
            throw new PersistenceException($"The history database could not be opened or created at '{DatabasePath}'.", ex);
        }
    }
}

public sealed class PersistenceException(string message, Exception inner) : Exception(message, inner);

/// <summary>Used only by "dotnet ef" at design time to create migrations.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PcHealthDbContext>
{
    public PcHealthDbContext CreateDbContext(string[] args) =>
        new(PcHealthDatabase.CreateOptions("Data Source=design-time.db"));
}
