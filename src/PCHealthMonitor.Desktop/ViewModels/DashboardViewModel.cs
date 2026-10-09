using System.Collections.ObjectModel;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using PCHealthMonitor.Application.Analysis;
using PCHealthMonitor.Application.Formatting;
using PCHealthMonitor.Application.Monitoring;
using PCHealthMonitor.Desktop.Infrastructure;
using PCHealthMonitor.Domain.Health;
using PCHealthMonitor.Domain.Machine;
using PCHealthMonitor.Domain.Metrics;

namespace PCHealthMonitor.Desktop.ViewModels;

/// <summary>
/// Live dashboard. Metrics are collected on a background task by <see cref="MonitorService"/> (never on the
/// UI thread) and classified by the same <see cref="HealthAnalyzer"/> used by status/scan.
/// Nothing shown here is saved to history.
/// </summary>
public sealed class DashboardViewModel : ObservableObject, IDisposable
{
    private const int HistoryCapacity = 60;
    private static readonly TimeSpan TemperatureRefresh = TimeSpan.FromSeconds(30);

    private readonly DesktopHost _host;
    private readonly Dispatcher _dispatcher;
    private readonly HealthAnalyzer _analyzer;
    private readonly List<double> _cpuHistory = [];
    private CancellationTokenSource? _cts;
    private Task? _loop;

    private SystemInfo? _system;
    private CpuInfo _cpuInfo = new(null, Environment.ProcessorCount);
    private TemperatureReading? _temperature;
    private DateTime _temperatureReadAt = DateTime.MinValue;

    private bool _isLoading = true;
    private string? _errorMessage;
    private string _machine = Environment.MachineName;
    private string _windows = "…";
    private string _uptime = "…";
    private string _lastUpdated = "…";
    private string _overall = "UNKNOWN";
    private string _overallReason = "Collecting metrics…";
    private bool _isIncomplete;
    private string _cpuValue = "—";
    private double _cpuPercent;
    private string _cpuStatus = "UNKNOWN";
    private string _cpuModel = "—";
    private string _cpuDetail = "—";
    private IReadOnlyList<double> _cpuSeries = [];
    private string _memoryValue = "—";
    private double _memoryPercent;
    private string _memoryStatus = "UNKNOWN";
    private string _memoryDetail = "—";
    private string _memorySub = "—";
    private string _temperatureValue = "—";
    private string _temperatureStatus = "UNKNOWN";
    private string _temperatureDetail = "Reading thermal zones…";

    public DashboardViewModel(DesktopHost host, Dispatcher dispatcher)
    {
        _host = host;
        _dispatcher = dispatcher;
        _analyzer = host.Services.CreateAnalyzer();
        RefreshSeconds = host.Options.Monitor.RefreshIntervalSeconds;
    }

    public double RefreshSeconds { get; }

    public string RefreshText => $"Live · refresh every {Display.Number(RefreshSeconds)} s · not saved to history";

    public ObservableCollection<DriveCardViewModel> Drives { get; } = [];

    public ObservableCollection<MessageViewModel> Alerts { get; } = [];

    public ObservableCollection<MessageViewModel> Notes { get; } = [];

    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }

    public string? ErrorMessage { get => _errorMessage; private set => Set(ref _errorMessage, value); }

    public string Machine { get => _machine; private set => Set(ref _machine, value); }

    public string Windows { get => _windows; private set => Set(ref _windows, value); }

    public string Uptime { get => _uptime; private set => Set(ref _uptime, value); }

    public string LastUpdated { get => _lastUpdated; private set => Set(ref _lastUpdated, value); }

    public string Overall { get => _overall; private set => Set(ref _overall, value); }

    public string OverallReason { get => _overallReason; private set => Set(ref _overallReason, value); }

    public bool IsIncomplete { get => _isIncomplete; private set => Set(ref _isIncomplete, value); }

    public string CpuValue { get => _cpuValue; private set => Set(ref _cpuValue, value); }

    public double CpuPercent { get => _cpuPercent; private set => Set(ref _cpuPercent, value); }

    public string CpuStatus { get => _cpuStatus; private set => Set(ref _cpuStatus, value); }

    public string CpuModel { get => _cpuModel; private set => Set(ref _cpuModel, value); }

    public string CpuDetail { get => _cpuDetail; private set => Set(ref _cpuDetail, value); }

    public IReadOnlyList<double> CpuSeries { get => _cpuSeries; private set => Set(ref _cpuSeries, value); }

    public string MemoryValue { get => _memoryValue; private set => Set(ref _memoryValue, value); }

    public double MemoryPercent { get => _memoryPercent; private set => Set(ref _memoryPercent, value); }

    public string MemoryStatus { get => _memoryStatus; private set => Set(ref _memoryStatus, value); }

    public string MemoryDetail { get => _memoryDetail; private set => Set(ref _memoryDetail, value); }

    public string MemorySub { get => _memorySub; private set => Set(ref _memorySub, value); }

    public string TemperatureValue { get => _temperatureValue; private set => Set(ref _temperatureValue, value); }

    public string TemperatureStatus { get => _temperatureStatus; private set => Set(ref _temperatureStatus, value); }

    public string TemperatureDetail { get => _temperatureDetail; private set => Set(ref _temperatureDetail, value); }

    /// <summary>Starts the background monitoring loop (idempotent).</summary>
    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => RunAsync(token), token);
    }

    /// <summary>Stops the loop; waits briefly so no collection keeps running after the window closes.</summary>
    public void Stop()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // Already logged by the loop.
        }

        _cts.Dispose();
        _cts = null;
        _loop = null;
    }

    public void Dispose() => Stop();

    private async Task RunAsync(CancellationToken token)
    {
        var services = _host.Services;
        try
        {
            _system = TryCollect(() => services.SystemInfo.Collect(), "system information");
            _cpuInfo = TryCollect(() => services.Cpu.GetInfo(), "CPU information") ?? _cpuInfo;

            await services.CreateMonitor().RunAsync(
                TimeSpan.FromSeconds(RefreshSeconds),
                async (tick, ct) =>
                {
                    RefreshTemperatureIfDue();
                    var snapshot = LiveHealth.ToSnapshot(tick, _cpuInfo, TimeSpan.FromSeconds(RefreshSeconds), _system, _temperature);
                    var assessment = _analyzer.Analyze(snapshot);
                    await _dispatcher.InvokeAsync(() => Apply(tick, snapshot, assessment), DispatcherPriority.Background, ct);
                },
                maxTicks: null,
                token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Window closed.
        }
        catch (Exception ex)
        {
            _host.Logger.LogError(ex, "Dashboard monitoring loop stopped unexpectedly.");
            await _dispatcher.InvokeAsync(() =>
            {
                IsLoading = false;
                ErrorMessage = "Live monitoring stopped because of an unexpected error. Details were written to the log file.";
            });
        }
    }

    private void RefreshTemperatureIfDue()
    {
        if (DateTime.UtcNow - _temperatureReadAt < TemperatureRefresh)
        {
            return;
        }

        _temperatureReadAt = DateTime.UtcNow;
        _temperature = TryCollect(() => _host.Services.Temperature.Read(), "temperature")
            ?? TemperatureReading.Unavailable("Temperature provider failed.");
    }

    private T? TryCollect<T>(Func<T> collect, string what)
        where T : class
    {
        try
        {
            return collect();
        }
        catch (Exception ex)
        {
            _host.Logger.LogWarning(ex, "Dashboard: {What} could not be collected.", what);
            return null;
        }
    }

    private void Apply(MonitorTick tick, HealthSnapshot snapshot, HealthAssessment assessment)
    {
        IsLoading = false;
        ErrorMessage = null;
        LastUpdated = tick.Timestamp.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

        if (snapshot.System is { } system)
        {
            Machine = system.Hostname;
            var build = system.Build is null ? string.Empty : $" · build {system.Build} · {system.Architecture}";
            Windows = system.WindowsDescription + build;
        }

        Uptime = Display.Uptime(tick.Uptime);

        // CPU
        CpuModel = _cpuInfo.Name ?? "CPU model unavailable";
        CpuDetail = $"{_cpuInfo.LogicalProcessorCount} logical processors";
        CpuStatus = assessment.StatusOf(HealthComponent.Cpu).ToLabel();
        if (tick.CpuPercent is { } cpu)
        {
            CpuValue = Display.Percent(cpu);
            CpuPercent = cpu;
            _cpuHistory.Add(cpu);
            if (_cpuHistory.Count > HistoryCapacity)
            {
                _cpuHistory.RemoveAt(0);
            }

            CpuSeries = _cpuHistory.ToArray();
        }
        else
        {
            CpuValue = "Unavailable";
            CpuPercent = 0;
        }

        // Memory
        MemoryStatus = assessment.StatusOf(HealthComponent.Memory).ToLabel();
        if (tick.Memory is { TotalBytes: > 0 } memory)
        {
            MemoryValue = Display.Percent(memory.UsagePercent);
            MemoryPercent = memory.UsagePercent;
            MemoryDetail = $"{Display.Bytes(memory.UsedBytes)} of {Display.Bytes(memory.TotalBytes)} in use";
            MemorySub = memory.InstalledBytes is { } installed && installed != memory.TotalBytes
                ? $"{Display.Bytes(memory.AvailableBytes)} available · {Display.Bytes(installed)} installed"
                : $"{Display.Bytes(memory.AvailableBytes)} available";
        }
        else
        {
            MemoryValue = "Unavailable";
            MemoryPercent = 0;
            MemoryDetail = "Memory information could not be read.";
            MemorySub = string.Empty;
        }

        UpdateDrives(snapshot.Storage, assessment);
        UpdateTemperature(snapshot.Temperature, assessment);

        // Overall + messages
        Overall = assessment.Overall.ToLabel();
        IsIncomplete = assessment.IsIncomplete;
        var reasons = assessment.Reasons.ToList();
        OverallReason = reasons.Count > 0
            ? reasons[0].Text
            : "All monitored components are within the configured thresholds.";

        Sync(Alerts, reasons.Select(m => new MessageViewModel(m.Severity.ToLabel(), m.Component.ToLabel(), m.Text)).ToList());
        Sync(Notes, assessment.Messages.Where(m => m.Severity == MessageSeverity.Info)
            .Select(m => new MessageViewModel("INFO", m.Component.ToLabel(), m.Text)).ToList());
    }

    private void UpdateDrives(StorageSnapshot? storage, HealthAssessment assessment)
    {
        var cards = new List<DriveCardViewModel>();
        if (storage is not null)
        {
            foreach (var drive in storage.Drives)
            {
                cards.Add(new DriveCardViewModel(
                    drive.Name,
                    drive.Label is null ? $"{drive.FileSystem ?? "?"} · local fixed drive" : $"{drive.Label} · {drive.FileSystem ?? "?"}",
                    Display.Percent(drive.UsagePercent),
                    drive.UsagePercent,
                    (assessment.DriveStatus(drive.Name) ?? HealthStatus.Unknown).ToLabel(),
                    $"{Display.Bytes(drive.FreeBytes)} free of {Display.Bytes(drive.TotalBytes)}"));
            }

            foreach (var name in storage.UnreadableDrives)
            {
                cards.Add(new DriveCardViewModel(name, "local fixed drive", "Unavailable", 0, "UNKNOWN",
                    "Drive not ready or inaccessible (e.g. locked)."));
            }
        }

        if (cards.Count == 0)
        {
            cards.Add(new DriveCardViewModel("Storage", "local fixed drives", "Unavailable", 0, "UNKNOWN", "No local fixed drive could be read."));
        }

        Sync(Drives, cards);
    }

    private void UpdateTemperature(TemperatureReading? reading, HealthAssessment assessment)
    {
        TemperatureStatus = assessment.StatusOf(HealthComponent.Temperature).ToLabel();
        if (reading is { IsAvailable: true } && reading.MaxCelsius is { } max)
        {
            var hottest = reading.Zones.MaxBy(z => z.Celsius)!;
            TemperatureValue = Display.Celsius(max);
            TemperatureDetail = $"ACPI thermal zone {hottest.Name} (firmware). Not necessarily the CPU core sensor."
                + (reading.ThrottlingReported ? " Windows reports thermal throttling." : string.Empty);
        }
        else
        {
            TemperatureValue = "Unavailable";
            TemperatureDetail = (reading?.UnavailableReason ?? "Temperature could not be obtained.")
                + " No value is estimated or invented.";
        }
    }

    /// <summary>Replaces collection content only when it changed (avoids flicker).</summary>
    private static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        if (target.SequenceEqual(items))
        {
            return;
        }

        if (target.Count == items.Count)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (!Equals(target[i], items[i]))
                {
                    target[i] = items[i];
                }
            }

            return;
        }

        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}

public sealed record DriveCardViewModel(string Name, string Subtitle, string Value, double Percent, string Status, string Detail);

public sealed record MessageViewModel(string Severity, string Component, string Text);
