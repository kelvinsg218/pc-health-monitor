using System.Runtime.InteropServices;
using System.Windows.Threading;
using PCHealthMonitor.Application.Formatting;
using PCHealthMonitor.Desktop.Infrastructure;

namespace PCHealthMonitor.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly DesktopHost _host;
    private int _selectedTab;

    public MainViewModel(DesktopHost host, Dispatcher dispatcher)
    {
        _host = host;
        Dashboard = new DashboardViewModel(host, dispatcher);
        Processes = new ProcessesViewModel(host);
        History = new HistoryViewModel(host);
        Scan = new ScanViewModel(host, History.Invalidate);
        OpenDataFolderCommand = new RelayCommand(() => ShellActions.OpenFolder(host.DataRoot));
        OpenLogsFolderCommand = new RelayCommand(() => ShellActions.OpenFolder(host.Services.LogsDirectory));
    }

    public string Title { get; } = "PC Health Monitor";

    public string Version { get; } = DesktopHost.Version;

    public DashboardViewModel Dashboard { get; }

    public ProcessesViewModel Processes { get; }

    public ScanViewModel Scan { get; }

    public HistoryViewModel History { get; }

    public RelayCommand OpenDataFolderCommand { get; }

    public RelayCommand OpenLogsFolderCommand { get; }

    public string? ConfigurationWarning => _host.ConfigurationWarning;

    public string DataFolder => _host.DataRoot;

    public string ReportsFolder => _host.Services.ReportsDirectory;

    public string DatabaseFile => System.IO.Path.Combine(_host.Services.DataDirectory, _host.Options.Paths.DatabaseFileName);

    public string LogsFolder => _host.Services.LogsDirectory;

    public string RuntimeText { get; } = $".NET {Environment.Version} · {RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture}";

    public string ThresholdsText
    {
        get
        {
            var t = _host.Options.Thresholds;
            return $"CPU {Display.Number(t.CpuWarning)}% / {Display.Number(t.CpuCritical)}%   ·   " +
                   $"Memory {Display.Number(t.MemoryWarning)}% / {Display.Number(t.MemoryCritical)}%   ·   " +
                   $"Storage {Display.Number(t.StorageWarning)}% / {Display.Number(t.StorageCritical)}%   ·   " +
                   $"Temperature {Display.Number(t.TemperatureWarning)} / {Display.Number(t.TemperatureCritical)} °C";
        }
    }

    /// <summary>0 Dashboard, 1 Processes, 2 Full scan, 3 History, 4 About.</summary>
    public int SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (Set(ref _selectedTab, value))
            {
                if (value == 1)
                {
                    Processes.EnsureLoaded();
                }
                else if (value == 3)
                {
                    History.EnsureLoaded();
                }
            }
        }
    }

    public void Start() => Dashboard.Start();

    /// <summary>Stops background work: cancels a running scan and the live monitoring loop.</summary>
    public void Dispose()
    {
        Scan.Dispose();
        Dashboard.Dispose();
    }
}
