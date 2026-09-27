using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PCHealthMonitor.Infrastructure.Persistence;

namespace PCHealthMonitor.Tests.Persistence;

/// <summary>
/// Private in-memory SQLite database (kept alive by an open connection) with the real
/// migrations applied. Never touches the user's data/pchealth.db.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using var db = CreateContext();
        db.Database.Migrate();
    }

    public PcHealthDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PcHealthDbContext>().UseSqlite(_connection).Options);

    public SqliteScanRepository CreateRepository() => new(CreateContext);

    public void Dispose() => _connection.Dispose();
}
