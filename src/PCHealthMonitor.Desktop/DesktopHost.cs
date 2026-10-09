using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using PCHealthMonitor.Application.Configuration;
using PCHealthMonitor.Infrastructure.Composition;

namespace PCHealthMonitor.Desktop;

/// <summary>
/// Desktop composition: same services as the CLI, with data stored per user in
/// %LOCALAPPDATA%\PCHealthMonitor (data, reports, logs) so the .exe can live anywhere (e.g. Downloads).
/// </summary>
public sealed class DesktopHost : IDisposable
{
    private DesktopHost(PcHealthOptions options, string dataRoot, string? configurationWarning)
    {
        DataRoot = dataRoot;
        ConfigurationWarning = configurationWarning;
        Services = new AppServices(options, dataRoot, Version);
        Logger = Services.LoggerFactory.CreateLogger("PCHealthMonitor.Desktop");
    }

    public static string Version
    {
        get
        {
            var informational = typeof(DesktopHost).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? informational[..plus] : informational;
        }
    }

    public static string DefaultDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCHealthMonitor");

    public AppServices Services { get; }

    public PcHealthOptions Options => Services.Options;

    public ILogger Logger { get; }

    public string DataRoot { get; }

    /// <summary>Set when appsettings.json was invalid and the defaults are being used instead.</summary>
    public string? ConfigurationWarning { get; }

    /// <summary>
    /// Loads optional settings (appsettings.json next to the .exe, PCHEALTH_ environment variables).
    /// An invalid configuration never prevents the app from opening: defaults are used and the problem is shown.
    /// </summary>
    public static DesktopHost Create(string? dataRoot = null)
    {
        PcHealthOptions options;
        string? warning = null;
        try
        {
            options = ConfigurationLoader.Load(AppContext.BaseDirectory);
            options.EnsureValid();
        }
        catch (InvalidConfigurationException ex)
        {
            options = new PcHealthOptions();
            warning = "The configuration file is invalid, so default settings are being used: " + string.Join(" ", ex.Errors);
        }

        var root = dataRoot ?? DefaultDataRoot;
        Directory.CreateDirectory(root);
        return new DesktopHost(options, root, warning);
    }

    public void Dispose() => Services.Dispose();
}
