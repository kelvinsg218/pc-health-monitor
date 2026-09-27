using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PCHealthMonitor.Infrastructure.Logging;

/// <summary>
/// Minimal append-only file logger: logs/pchealth-YYYYMMDD.log. Technical details (exceptions)
/// go here instead of the console. If the log file cannot be written, logging is silently skipped.
/// </summary>
public sealed class FileLoggerProvider(string logsDirectory, LogLevel minimumLevel) : ILoggerProvider
{
    private readonly object _sync = new();
    private readonly LogLevel _minimumLevel = minimumLevel;
    private bool _disabled;

    public string LogsDirectory { get; } = Path.GetFullPath(logsDirectory);

    public string CurrentLogFile => Path.Combine(LogsDirectory, $"pchealth-{DateTime.Now:yyyyMMdd}.log");

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(string category, LogLevel level, string message, Exception? exception)
    {
        var sb = new StringBuilder();
        sb.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
            .Append(" [").Append(Abbreviate(level)).Append("] ")
            .Append(category).Append(": ").Append(message).AppendLine();
        if (exception is not null)
        {
            sb.Append("    ").Append(exception.ToString().Replace("\n", "\n    ", StringComparison.Ordinal)).AppendLine();
        }

        lock (_sync)
        {
            if (_disabled)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(LogsDirectory);
                File.AppendAllText(CurrentLogFile, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _disabled = true; // never crash the tool because of logging
            }
        }
    }

    private static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "---",
    };

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= provider._minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                provider.Write(category, logLevel, formatter(state, exception), exception);
            }
        }
    }
}
