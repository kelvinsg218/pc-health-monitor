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
        $"Amostra a CPU a cada {PtBr.Number(_host.Options.Scan.SampleIntervalSeconds)} s durante {PtBr.Number(_host.Options.Scan.DurationSeconds)} s, " +
        "mede os processos na mesma janela, analisa memória, unidades e temperatura, salva o resultado no histórico local " +
        "e compara com a verificação anterior deste computador.";

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
        ProgressText = "Iniciando a verificação completa…";
        var progress = new Progress<string>(text => ProgressText = PtBr.Message(text));
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
            ProgressText = "Verificação cancelada. Nada foi salvo.";
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
        ResultHeader = $"Verificação de {result.Hostname} · {PtBr.DateTime(result.StartedAt)}"
            + (assessment.IsIncomplete ? " · dados incompletos" : string.Empty);
        ProgressText = "Verificação concluída.";

        Components.Clear();
        var cpu = snapshot.Cpu;
        Components.Add(Row(assessment, HealthComponent.Cpu, "CPU",
            cpu is null ? PtBr.Unavailable : $"média {PtBr.Percent(cpu.Usage.Average)} · pico {PtBr.Percent(cpu.Usage.Peak)} · mín. {PtBr.Percent(cpu.Usage.Minimum)}"));
        Components.Add(Row(assessment, HealthComponent.Memory, "Memória",
            snapshot.Memory is { TotalBytes: > 0 } m ? $"{PtBr.Percent(m.UsagePercent)} · {PtBr.Bytes(m.UsedBytes)} de {PtBr.Bytes(m.TotalBytes)}" : PtBr.Unavailable));
        foreach (var drive in assessment.Drives)
        {
            var metrics = snapshot.Storage?.Drives.FirstOrDefault(d => d.Name == drive.Name);
            var value = metrics is null
                ? PtBr.Unavailable
                : $"{PtBr.Percent(metrics.UsagePercent)} em uso, {PtBr.Bytes(metrics.FreeBytes)} livres de {PtBr.Bytes(metrics.TotalBytes)}";
            Components.Add(new ComponentRowViewModel($"Unidade {drive.Name}", drive.Status.ToLabel(), value));
        }

        if (assessment.Drives.Count == 0)
        {
            Components.Add(Row(assessment, HealthComponent.Storage, "Armazenamento", PtBr.Unavailable));
        }

        Components.Add(Row(assessment, HealthComponent.Temperature, "Temperatura",
            snapshot.Temperature?.MaxCelsius is { } t ? $"{PtBr.Celsius(t)} (zona térmica ACPI)" : PtBr.Unavailable));

        Messages.Clear();
        foreach (var message in assessment.Messages.OrderByDescending(x => x.Severity))
        {
            Messages.Add(new MessageViewModel(message.Severity.ToLabel(), PtBr.Component(message.Component.ToLabel()), PtBr.Message(message.Text)));
        }

        Changes.Clear();
        ComparisonHeader = null;
        if (outcome.Comparison is { } comparison)
        {
            ComparisonHeader = $"Mudança observada desde a verificação nº {comparison.Previous.Id} ({PtBr.DateTimeShort(comparison.Previous.StartedAt)}) — não é uma previsão";
            foreach (var row in ChangeRowViewModel.From(comparison))
            {
                Changes.Add(row);
            }
        }

        SavedText = outcome.SavedScan is { } saved
            ? $"Salva no histórico como verificação nº {saved.Id}."
            : PtBr.Message(outcome.HistoryError);
        ReportPath = outcome.Report?.JsonPath;
        if (outcome.ReportError is { } reportError)
        {
            ErrorMessage = PtBr.Message(reportError);
        }
    }

    private static ComponentRowViewModel Row(Domain.Health.HealthAssessment assessment, HealthComponent component, string name, string value) =>
        new(name, assessment.StatusOf(component).ToLabel(), value);

    private void OnError(Exception ex)
    {
        _host.Logger.LogError(ex, "Full scan failed.");
        ErrorMessage = "Não foi possível concluir a verificação. Os detalhes foram gravados no arquivo de log.";
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
                MetricName(change.Metric),
                PtBr.Percent(change.Previous),
                PtBr.Percent(change.Current),
                change.DeltaPoints is { } d ? PtBr.PercentagePoints(d) : "n/d");
        }

        yield return new ChangeRowViewModel(
            "Saúde geral",
            PtBr.Status(comparison.Previous.OverallHealth.ToLabel()),
            PtBr.Status(comparison.Current.OverallHealth.ToLabel()),
            comparison.OverallChanged ? "mudou" : "sem mudança");
    }

    private static string MetricName(string metric) => metric switch
    {
        "CPU average" => "CPU (média)",
        "CPU peak" => "CPU (pico)",
        "Memory usage" => "Uso de memória",
        _ when metric.StartsWith("Drive ", StringComparison.Ordinal) && metric.EndsWith(" usage", StringComparison.Ordinal)
            => $"Uso da unidade {metric[6..^6]}",
        _ => metric,
    };
}
