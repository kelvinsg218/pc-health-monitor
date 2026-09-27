using PCHealthMonitor.Application.Configuration;
using PCHealthMonitor.Application.Scanning;

namespace PCHealthMonitor.Application.History;

public interface IScanRepository
{
    Task<int> AddAsync(ScanRecord scan, CancellationToken cancellationToken);

    /// <summary>Most recent scans first.</summary>
    Task<IReadOnlyList<ScanRecord>> ListRecentAsync(int limit, CancellationToken cancellationToken);

    Task<ScanRecord?> GetAsync(int id, CancellationToken cancellationToken);

    /// <summary>The latest scan of <paramref name="hostname"/> started before the given scan.</summary>
    Task<ScanRecord?> GetPreviousAsync(string hostname, int scanId, CancellationToken cancellationToken);

    Task<int> DeleteStartedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}

public sealed class HistoryService(IScanRepository repository, HistoryOptions options, TimeProvider timeProvider)
{
    /// <summary>Persists a full scan and applies the retention policy. Returns the new scan id.</summary>
    public async Task<ScanRecord> SaveAsync(HealthCheckResult result, CancellationToken cancellationToken)
    {
        var record = ScanRecord.FromResult(result);
        var id = await repository.AddAsync(record, cancellationToken).ConfigureAwait(false);

        if (options.RetentionDays > 0)
        {
            var cutoff = timeProvider.GetUtcNow().AddDays(-options.RetentionDays);
            await repository.DeleteStartedBeforeAsync(cutoff, cancellationToken).ConfigureAwait(false);
        }

        return record with { Id = id };
    }

    public Task<IReadOnlyList<ScanRecord>> ListAsync(int limit, CancellationToken cancellationToken) =>
        repository.ListRecentAsync(limit, cancellationToken);

    public Task<ScanRecord?> GetAsync(int id, CancellationToken cancellationToken) =>
        repository.GetAsync(id, cancellationToken);

    /// <summary>Compares a scan with the previous scan of the same machine, if any.</summary>
    public async Task<ScanComparison?> CompareWithPreviousAsync(ScanRecord current, CancellationToken cancellationToken)
    {
        var previous = await repository.GetPreviousAsync(current.Hostname, current.Id, cancellationToken).ConfigureAwait(false);
        return previous is null ? null : HistoryComparer.Compare(previous, current);
    }
}
