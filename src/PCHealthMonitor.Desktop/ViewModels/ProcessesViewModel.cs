using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using PCHealthMonitor.Application.Formatting;
using PCHealthMonitor.Desktop.Infrastructure;
using PCHealthMonitor.Domain.Metrics;

namespace PCHealthMonitor.Desktop.ViewModels;

/// <summary>Top processes by memory and by CPU (read-only view; nothing is changed or terminated).</summary>
public sealed class ProcessesViewModel : ObservableObject
{
    private readonly DesktopHost _host;
    private bool _isBusy;
    private bool _loadedOnce;
    private string _summary = "Ainda não medido.";
    private string? _errorMessage;

    public ProcessesViewModel(DesktopHost host)
    {
        _host = host;
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy, OnError);
    }

    public ObservableCollection<ProcessRowViewModel> TopMemory { get; } = [];

    public ObservableCollection<ProcessRowViewModel> TopCpu { get; } = [];

    public AsyncCommand RefreshCommand { get; }

    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public string? ErrorMessage { get => _errorMessage; private set => Set(ref _errorMessage, value); }

    public void EnsureLoaded()
    {
        if (!_loadedOnce)
        {
            _loadedOnce = true;
            RefreshCommand.Execute(null);
        }
    }

    private async Task RefreshAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var options = _host.Options.Processes;
            var window = TimeSpan.FromSeconds(options.CpuSampleSeconds);
            Summary = $"Medindo o uso de CPU durante {PtBr.Number(window.TotalSeconds)} s…";
            var summary = await Task.Run(() => _host.Services.CreateProcessService().GetTopProcessesAsync(window, options.TopCount, CancellationToken.None));
            Apply(summary);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(ProcessSummary summary)
    {
        TopMemory.Clear();
        foreach (var p in summary.TopByMemory)
        {
            TopMemory.Add(ProcessRowViewModel.From(p));
        }

        TopCpu.Clear();
        foreach (var p in summary.TopByCpu)
        {
            TopCpu.Add(ProcessRowViewModel.From(p));
        }

        var unavailable = summary.CpuUnavailableCount == 0
            ? "CPU medida para todos"
            : $"CPU indisponível para {summary.CpuUnavailableCount} (iniciados/encerrados durante a amostragem ou acesso negado)";
        Summary = $"{summary.TotalProcesses} processos analisados · {unavailable} · janela de CPU de {PtBr.Number(summary.CpuSampleWindow.TotalSeconds, "0.0")} s · atualizado às {DateTime.Now:HH:mm:ss}";
    }

    private void OnError(Exception ex)
    {
        _host.Logger.LogError(ex, "Process list could not be collected.");
        ErrorMessage = "Não foi possível coletar a lista de processos. Os detalhes foram gravados no arquivo de log.";
        Summary = "Indisponível.";
    }
}

public sealed record ProcessRowViewModel(int Pid, string Name, string Memory, string Cpu)
{
    public static ProcessRowViewModel From(ProcessUsage p) =>
        new(p.Pid, p.Name, PtBr.Bytes(p.WorkingSetBytes), PtBr.Percent(p.CpuPercent));
}
