using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace PCHealthMonitor.Desktop;

/// <summary>
/// Application entry: composes services, shows the main window and turns unexpected errors into
/// readable messages (technical details go to the log file, never to the user as a stack trace).
/// </summary>
public partial class App : System.Windows.Application
{
    private DesktopHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            MessageBox.Show("PC Health Monitor requires Windows 10 or Windows 11.", "PC Health Monitor",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        try
        {
            _host = DesktopHost.Create(ParseDataDirectory(e.Args));
            _host.Logger.LogInformation("PC Health Monitor desktop {Version} started.", DesktopHost.Version);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "PC Health Monitor could not start because its data folder could not be prepared:\n\n" + ex.Message,
                "PC Health Monitor", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var window = new MainWindow(_host);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Logger.LogInformation("PC Health Monitor desktop closed.");
        _host?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Optional "--data-dir &lt;folder&gt;": stores history, reports and logs in that folder instead of
    /// %LOCALAPPDATA%\PCHealthMonitor (portable use, e.g. from a USB drive).
    /// </summary>
    internal static string? ParseDataDirectory(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], "--data-dir", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(args[i + 1]))
            {
                return System.IO.Path.GetFullPath(args[i + 1]);
            }
        }

        return null;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Logger.LogError(e.Exception, "Unhandled UI exception.");
        e.Handled = true; // keep the application running
        MessageBox.Show(
            "An unexpected error occurred. The application will keep running.\n\n" + e.Exception.Message +
            (_host is null ? string.Empty : "\n\nTechnical details were written to:\n" + _host.Services.LogProvider.CurrentLogFile),
            "PC Health Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _host?.Logger.LogCritical(ex, "Unhandled exception.");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _host?.Logger.LogError(e.Exception, "Unobserved background task exception.");
        e.SetObserved();
    }
}
