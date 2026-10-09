using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using PCHealthMonitor.Application.Formatting;
using PCHealthMonitor.Application.History;
using PCHealthMonitor.Desktop.Infrastructure;
using PCHealthMonitor.Domain.Health;
using PCHealthMonitor.Infrastructure.Persistence;

namespace PCHealthMonitor.Desktop.ViewModels;

/// <summary>Saved full scans, details of the selected scan and comparison with the previous one.</summary>
public sealed class HistoryViewModel : ObservableObject
{
    private readonly DesktopHost _host;
    private bool _isBusy;
    private bool _loadedOnce;
    private string? _errorMessage;
    private string _summary = "Loading…";
    private ScanRowViewModel? _selected;
    private string? _detailHeader;
    private string? _comparisonHeader;
    private int _detailVersion;

    public HistoryViewModel(DesktopHost host)
    {
        _host = host;
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy, OnError);
    }

    public ObservableCollection<ScanRowViewModel> Scans { get; } = [];

    public ObservableCollection<ComponentRowViewModel> Details { get; } = [];

    public ObservableCollection<MessageViewModel> Messages { get; } = [];

    public ObservableCollection<ChangeRowViewModel> Changes { get; } = [];

    public AsyncCommand RefreshCommand { get; }

    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    public string? ErrorMessage { get => _errorMessage; private set => Set(ref _errorMessage, value); }

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public string? DetailHeader { get => _detailHeader; private set => Set(ref _detailHeader, value); }

    public string? ComparisonHeader { get => _comparisonHeader; private set => Set(ref _comparisonHeader, value); }

    public ScanRowViewModel? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                _ = LoadDetailsAsync(value);
            }
        }
    }

    public void EnsureLoaded()
    {
        if (!_loadedOnce)
        {
            _loadedOnce = true;
            RefreshCommand.Execute(null);
        }
    }

    /// <summary>Called after a scan is saved so the list is current the next time it is shown.</summary>
    public void Invalidate()
    {
        _loadedOnce = false;
    }

    private async Task RefreshAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var limit = _host.Options.History.DefaultListLimit * 5;
            var scans = await Task.Run(async () =>
            {
                var history = _host.Services.CreateHistory();
                return await history.ListAsync(limit, CancellationToken.None);
            });

            Scans.Clear();
            foreach (var scan in scans)
            {
                Scans.Add(ScanRowViewModel.From(scan));
            }

            Summary = scans.Count == 0
                ? "No scans yet. Run a full scan to start the history."
                : $"{scans.Count} scan(s), most recent first. Stored in {_host.Services.Database.DatabasePath}";
            Selected = Scans.FirstOrDefault();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadDetailsAsync(ScanRowViewModel? row)
    {
        var version = ++_detailVersion;
        Details.Clear();
        Messages.Clear();
        Changes.Clear();
        DetailHeader = null;
        ComparisonHeader = null;
        if (row is null)
        {
            return;
        }

        try
        {
            var (scan, comparison) = await Task.Run(async () =>
            {
                var history = _host.Services.CreateHistory();
                var record = await history.GetAsync(row.Id, CancellationToken.None);
                var cmp = record is null ? null : await history.CompareWithPreviousAsync(record, CancellationToken.None);
                return (record, cmp);
            });

            if (version != _detailVersion || scan is null)
            {
                return;
            }

            DetailHeader = $"Scan #{scan.Id} · {scan.Hostname} · {Display.LocalDateTime(scan.StartedAt)} · {scan.WindowsVersion ?? "Windows version unavailable"}";
            Details.Add(new ComponentRowViewModel("CPU", scan.CpuStatus.ToLabel(),
                $"avg {Display.Percent(scan.CpuAverage)} · peak {Display.Percent(scan.CpuPeak)} · min {Display.Percent(scan.CpuMinimum)}"));
            Details.Add(new ComponentRowViewModel("Memory", scan.MemoryStatus.ToLabel(),
                scan.MemoryUsagePercent is null ? "Unavailable" : $"{Display.Percent(scan.MemoryUsagePercent)} · {Display.Bytes(scan.MemoryUsedBytes)} of {Display.Bytes(scan.MemoryTotalBytes)}"));
            foreach (var drive in scan.Drives)
            {
                Details.Add(new ComponentRowViewModel($"Drive {drive.Name}", drive.Status.ToLabel(),
                    $"{Display.Percent(drive.UsagePercent)} used · {Display.Bytes(drive.FreeBytes)} free of {Display.Bytes(drive.TotalBytes)}"));
            }

            Details.Add(new ComponentRowViewModel("Temperature", scan.TemperatureStatus.ToLabel(), Display.Celsius(scan.TemperatureCelsius)));
            Details.Add(new ComponentRowViewModel("Overall", scan.OverallHealth.ToLabel(),
                scan.IsIncomplete ? "incomplete data" : (scan.UptimeSeconds is { } up ? $"uptime {Display.Uptime(TimeSpan.FromSeconds(up))}" : string.Empty)));

            foreach (var m in scan.Messages.OrderByDescending(m => m.Severity))
            {
                Messages.Add(new MessageViewModel(m.Severity.ToLabel(), m.Component.ToLabel(), m.Text));
            }

            if (comparison is not null)
            {
                ComparisonHeader = $"Observed change since scan #{comparison.Previous.Id} ({Display.LocalDateTimeShort(comparison.Previous.StartedAt)}) — not a prediction";
                foreach (var change in ChangeRowViewModel.From(comparison))
                {
                    Changes.Add(change);
                }
            }
        }
        catch (Exception ex)
        {
            OnError(ex);
        }
    }

    private void OnError(Exception ex)
    {
        _host.Logger.LogError(ex, "History could not be loaded.");
        ErrorMessage = ex is PersistenceException
            ? ex.Message
            : "The history could not be loaded. Details were written to the log file.";
        Summary = "History unavailable.";
    }
}

public sealed record ScanRowViewModel(int Id, string Date, string CpuAverage, string CpuPeak, string Memory, string DiskMax, string Health, string Incomplete)
{
    public static ScanRowViewModel From(ScanRecord scan)
    {
        var disk = scan.Drives.Count == 0
            ? "n/a"
            : scan.Drives.MaxBy(d => d.UsagePercent) is { } d ? $"{d.Name} {Display.Percent(d.UsagePercent)}" : "n/a";
        return new ScanRowViewModel(
            scan.Id,
            Display.LocalDateTimeShort(scan.StartedAt),
            Display.Percent(scan.CpuAverage),
            Display.Percent(scan.CpuPeak),
            Display.Percent(scan.MemoryUsagePercent),
            disk,
            scan.OverallHealth.ToLabel(),
            scan.IsIncomplete ? "incomplete" : string.Empty);
    }
}
