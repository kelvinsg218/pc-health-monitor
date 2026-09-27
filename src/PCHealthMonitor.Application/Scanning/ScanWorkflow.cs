using Microsoft.Extensions.Logging;
using PCHealthMonitor.Application.Configuration;
using PCHealthMonitor.Application.History;
using PCHealthMonitor.Application.Reports;

namespace PCHealthMonitor.Application.Scanning;

public sealed record ScanWorkflowRequest(ScanSettings Settings, bool SaveToHistory, bool WriteReport);

/// <summary>
/// Result of a full scan workflow. Persistence and report failures are reported here
/// (with friendly messages) without discarding the scan result itself.
/// </summary>
public sealed record ScanOutcome(
    HealthCheckResult Result,
    ScanRecord? SavedScan,
    string? HistoryError,
    ScanComparison? Comparison,
    ReportFiles? Report,
    string? ReportError);

/// <summary>Full scan → persist → compare with previous scan → optional report.</summary>
public sealed class ScanWorkflow(
    HealthCheckService healthCheck,
    Func<HistoryService> historyFactory,
    IReportWriter reportWriter,
    PcHealthOptions options,
    string toolVersion,
    ILogger<ScanWorkflow> logger)
{
    public async Task<ScanOutcome> RunAsync(ScanWorkflowRequest request, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await healthCheck.FullScanAsync(request.Settings, progress, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Scan finished: overall {Overall}, incomplete {Incomplete}.", result.Assessment.Overall, result.Assessment.IsIncomplete);

        ScanRecord? saved = null;
        ScanComparison? comparison = null;
        string? historyError = null;
        if (request.SaveToHistory)
        {
            try
            {
                var history = historyFactory();
                saved = await history.SaveAsync(result, cancellationToken).ConfigureAwait(false);
                comparison = await history.CompareWithPreviousAsync(saved, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not save the scan to the history database.");
                historyError = "The scan could not be saved to the history database. See the log file for details.";
            }
        }

        ReportFiles? report = null;
        string? reportError = null;
        if (request.WriteReport)
        {
            try
            {
                var document = ScanReportBuilder.Build(result, saved?.Id, toolVersion, options.Thresholds);
                report = await reportWriter.WriteAsync(document, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not write the report.");
                reportError = "The report could not be written. See the log file for details.";
            }
        }

        return new ScanOutcome(result, saved, historyError, comparison, report, reportError);
    }
}
