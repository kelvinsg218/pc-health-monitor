using Microsoft.Extensions.Logging;
using PCHealthMonitor.Application.Abstractions;
using PCHealthMonitor.Application.Analysis;
using PCHealthMonitor.Application.Configuration;
using PCHealthMonitor.Application.History;
using PCHealthMonitor.Application.Monitoring;
using PCHealthMonitor.Application.Processes;
using PCHealthMonitor.Application.Scanning;
using PCHealthMonitor.Infrastructure.Logging;
using PCHealthMonitor.Infrastructure.Persistence;
using PCHealthMonitor.Infrastructure.Reports;
using PCHealthMonitor.Infrastructure.Windows;

namespace PCHealthMonitor.Infrastructure.Composition;

/// <summary>
/// Composition root. Objects are created on demand so that commands which do not need the
/// database (status, monitor, processes) never create or open it.
/// </summary>
public sealed class AppServices : IDisposable
{
    private readonly Lazy<PcHealthDatabase> _database;

    public AppServices(PcHealthOptions options, string workingDirectory, string toolVersion)
    {
        Options = options;
        ToolVersion = toolVersion;
        DataDirectory = Path.GetFullPath(options.Paths.DataDirectory, workingDirectory);
        ReportsDirectory = Path.GetFullPath(options.Paths.ReportsDirectory, workingDirectory);
        LogsDirectory = Path.GetFullPath(options.Paths.LogsDirectory, workingDirectory);

        LogProvider = new FileLoggerProvider(LogsDirectory, LogLevel.Information);
        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddFilter("Microsoft", LogLevel.Warning)
            .AddProvider(LogProvider));

        SystemInfo = new WindowsSystemInfoCollector(TimeProvider);
        Cpu = new WindowsCpuCollector();
        Memory = new WindowsMemoryCollector();
        Storage = new DriveStorageCollector(LoggerFactory.CreateLogger<DriveStorageCollector>());
        Temperature = new ThermalZoneTemperatureProvider(LoggerFactory.CreateLogger<ThermalZoneTemperatureProvider>());
        Processes = new WindowsProcessCollector(LoggerFactory.CreateLogger<WindowsProcessCollector>());

        _database = new Lazy<PcHealthDatabase>(() => new PcHealthDatabase(
            Path.Combine(DataDirectory, options.Paths.DatabaseFileName),
            LoggerFactory.CreateLogger<PcHealthDatabase>()));
    }

    public PcHealthOptions Options { get; }

    public string ToolVersion { get; }

    public string DataDirectory { get; }

    public string ReportsDirectory { get; }

    public string LogsDirectory { get; }

    public FileLoggerProvider LogProvider { get; }

    public ILoggerFactory LoggerFactory { get; }

    public TimeProvider TimeProvider { get; } = TimeProvider.System;

    public ISystemInfoCollector SystemInfo { get; }

    public ICpuCollector Cpu { get; }

    public IMemoryCollector Memory { get; }

    public IStorageCollector Storage { get; }

    public ITemperatureProvider Temperature { get; }

    public IProcessCollector Processes { get; }

    public PcHealthDatabase Database => _database.Value;

    public HealthAnalyzer CreateAnalyzer() => new(Options.Thresholds);

    public HealthCheckService CreateHealthCheck() => new(
        SystemInfo, Cpu, Memory, Storage, Temperature, Processes, CreateAnalyzer(), TimeProvider,
        LoggerFactory.CreateLogger<HealthCheckService>());

    public MonitorService CreateMonitor() => new(
        Cpu, Memory, Storage, SystemInfo, Options.Thresholds, TimeProvider, LoggerFactory.CreateLogger<MonitorService>());

    public ProcessService CreateProcessService() => new(Processes, Cpu);

    /// <summary>Opens (and on first use creates/migrates) the SQLite history database.</summary>
    public HistoryService CreateHistory()
    {
        Database.EnsureInitialized();
        return new HistoryService(new SqliteScanRepository(Database.CreateContext), Options.History, TimeProvider);
    }

    public ScanWorkflow CreateScanWorkflow() => new(
        CreateHealthCheck(),
        CreateHistory,
        new FileReportWriter(ReportsDirectory),
        Options,
        ToolVersion,
        LoggerFactory.CreateLogger<ScanWorkflow>());

    public void Dispose()
    {
        LoggerFactory.Dispose();
        LogProvider.Dispose();
    }
}
