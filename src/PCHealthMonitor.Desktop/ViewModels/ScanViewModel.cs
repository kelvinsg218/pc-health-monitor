using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using PCHealthMonitor.Application.Formatting;
using PCHealthMonitor.Application.History;
using PCHealthMonitor.Application.Scanning;
using PCHealthMonitor.Desktop.Infrastructure;
using PCHealthMonitor.Domain.Health;

namespace PCHealthMonitor.Desktop.ViewModels;

/// <summary>Full scan (saved to history) with optional JSON/TXT report.</summary>
public sealed class ScanViewModel : ObservableObject, IDisposable
{
    private readonly DesktopHost _host;
    private readonly Action _scanSaved;
    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private bool _generateReport = true;
    private string _progressText = string.Empty;
    private bool _hasResult;
    private string _overall = "UNKNOWN";
    private string _resultHeader = string.Empty;
    private string? _savedText;
    private string? _reportPath;
    private string? _errorMessage;
    private string? _comparisonHeader;

    public ScanViewModel(DesktopHost host, Action scanSaved)
    {
        _host = host;
        _scanSaved = scanSaved;
        RunCommand = new AsyncCommand(RunAsync, () => !IsRunning, OnError);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsRunning);
        OpenReportsCommand = new RelayCommand(() => ShellActions.OpenFolder(_host.Services.ReportsDirectory, _reportPath));
    }

    public AsyncCommand RunCommand { get; }

    /// <summary>Cancels a running scan (used when the window closes).</summary>
    public void Dispose() => _cts?.Cancel();

    public RelayCommand CancelCommand { get; }

    public RelayCommand OpenReportsCommand { get; }

    public string Description =>
        $"Samples CPU every {Display.Number(_host.Options.Scan.SampleIntervalSeconds)} s for {Display.Number(_host.Options.Scan.DurationSeconds)} s, " +
        "measures processes over the same window, analyzes memory, drives and temperature, saves the result to the local history " +
        "and compares it with the previous scan of this computer.";

    public bool GenerateReport { get => _generateReport; set => Set(ref _generateReport, value); }

    public bool IsRunning { get => _isRunning; private set => Set(ref _isRunning, value); }

    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }

    public bool HasResult { get => _hasResult; private set => Set(ref _hasResult, value); }

    public string Overall { get => _overall; private set => Set(ref _overall, value); }

    public string ResultHeader { get => _resultHeader; private set => Set(ref _resultHeader, value); }

    public string? SavedText { get => _savedText; private set => Set(ref _savedText, value); }

    public string? ReportPath { get => _reportPath; private set => Set(ref _reportPath, value); }

    public string? ErrorMessage { get => _errorMessage; private set => Set(ref _errorMessage, value); }

    public string? ComparisonHeader { get => _comparisonHeader; private set => Set(ref _comparisonHeader, value); }

    public ObservableCollection<ComponentRowViewModel> Components { get; } = [];

    public ObservableCollection<MessageViewModel> Messages { get; } = [];

    public ObservableCollection<ChangeRowViewModel> Changes { get; } = [];

    private async Task RunAsync()
    {
        var options = _host.Options;
        var settings = new ScanSettings(
            TimeSpan.FromSeconds(options.Scan.DurationSeconds),
            TimeSpan.FromSeconds(options.Scan.SampleIntervalSeconds),
            options.Processes.TopCount);
        var request = new ScanWorkflowRequest(settings, SaveToHistory: true, WriteReport: GenerateReport);

        _cts = new CancellationTokenSource();
        IsRunning = true;
        ErrorMessage = null;
        ProgressText = "Starting full scan…";
        var progress = new Progress<string>(text => ProgressText = text);
        try
        {
            var token = _cts.Token;
            var outcome = await Task.Run(() => _host.Services.CreateScanWorkflow().RunAsync(request, progress, token), token);
            Apply(outcome);
            if (outcome.SavedScan is not null)
            {
                _scanSaved();
            }
        }
        catch (OperationCanceledException)
        {
            ProgressText = "Scan cancelled. Nothing was saved.";
        }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void Apply(ScanOutcome outcome)
    {
        var result = outcome.Result;
        var snapshot = result.Snapshot;
        var assessment = result.Assessment;

        HasResult = true;
        Overall = assessment.Overall.ToLabel();
        ResultHeader = $"Scan of {result.Hostname} · {Display.LocalDateTime(result.StartedAt)}"
            + (assessment.IsIncomplete ? " · incomplete data" : string.Empty);
        ProgressText = "Scan finished.";

        Components.Clear();
        var cpu = snapshot.Cpu;
        Components.Add(Row(assessment, HealthComponent.Cpu, "CPU",
            cpu is null ? "Unavailable" : $"avg {Display.Percent(cpu.Usage.Average)} · peak {Display.Percent(cpu.Usage.Peak)} · min {Display.Percent(cpu.Usage.Minimum)}"));
        Components.Add(Row(assessment, HealthComponent.Memory, "Memory",
            snapshot.Memory is { TotalBytes: > 0 } m ? $"{Display.Percent(m.UsagePercent)} · {Display.Bytes(m.UsedBytes)} of {Display.Bytes(m.TotalBytes)}" : "Unavailable"));
        foreach (var drive in assessment.Drives)
        {
            Components.Add(new ComponentRowViewModel($"Drive {drive.Name}", drive.Status.ToLabel(), drive.Evidence));
        }

        if (assessment.Drives.Count == 0)
        {
            Components.Add(Row(assessment, HealthComponent.Storage, "Storage", "Unavailable"));
        }

        Components.Add(Row(assessment, HealthComponent.Temperature, "Temperature",
            snapshot.Temperature?.MaxCelsius is { } t ? $"{Display.Celsius(t)} (ACPI thermal zone)" : "Unavailable"));

        Messages.Clear();
        foreach (var message in assessment.Messages.OrderByDescending(x => x.Severity))
        {
            Messages.Add(new MessageViewModel(message.Severity.ToLabel(), message.Component.ToLabel(), message.Text));
        }

        Changes.Clear();
        ComparisonHeader = null;
        if (outcome.Comparison is { } comparison)
        {
            ComparisonHeader = $"Observed change since scan #{comparison.Previous.Id} ({Display.LocalDateTimeShort(comparison.Previous.StartedAt)}) — not a prediction";
            foreach (var row in ChangeRowViewModel.From(comparison))
            {
                Changes.Add(row);
            }
        }

        SavedText = outcome.SavedScan is { } saved
            ? $"Saved to history as scan #{saved.Id}."
            : outcome.HistoryError;
        ReportPath = outcome.Report?.JsonPath;
        if (outcome.ReportError is { } reportError)
        {
            ErrorMessage = reportError;
        }
    }

    private static ComponentRowViewModel Row(Domain.Health.HealthAssessment assessment, HealthComponent component, string name, string value) =>
        new(name, assessment.StatusOf(component).ToLabel(), value);

    private void OnError(Exception ex)
    {
        _host.Logger.LogError(ex, "Full scan failed.");
        ErrorMessage = "The scan could not be completed. Details were written to the log file.";
        ProgressText = string.Empty;
    }
}

public sealed record ComponentRowViewModel(string Name, string Status, string Value);

public sealed record ChangeRowViewModel(string Metric, string Previous, string Current, string Change)
{
    public static IEnumerable<ChangeRowViewModel> From(ScanComparison comparison)
    {
        foreach (var change in comparison.Changes)
        {
            yield return new ChangeRowViewModel(
                change.Metric,
                Display.Percent(change.Previous),
                Display.Percent(change.Current),
                change.DeltaPoints is { } d ? Display.PercentagePoints(d) : "n/a");
        }

        yield return new ChangeRowViewModel(
            "Overall health",
            comparison.Previous.OverallHealth.ToLabel(),
            comparison.Current.OverallHealth.ToLabel(),
            comparison.OverallChanged ? "changed" : "unchanged");
    }
}
