using Microsoft.Extensions.Configuration;
using PCHealthMonitor.Application.Configuration;

namespace PCHealthMonitor.Cli.Composition;

/// <summary>
/// Loads settings from appsettings.json (next to the executable, optional) and environment
/// variables prefixed with PCHEALTH_ (e.g. PCHEALTH_Scan__DurationSeconds=10). Unknown keys and
/// invalid values are reported as configuration errors instead of being silently ignored.
/// </summary>
public static class ConfigurationLoader
{
    public const string FileName = "appsettings.json";
    public const string EnvironmentPrefix = "PCHEALTH_";

    public static PcHealthOptions Load(string baseDirectory, IDictionary<string, string?>? overrides = null)
    {
        var options = new PcHealthOptions();
        try
        {
            var file = new ConfigurationBuilder()
                .SetBasePath(baseDirectory)
                .AddJsonFile(FileName, optional: true, reloadOnChange: false)
                .Build();
            file.GetSection(PcHealthOptions.SectionName).Bind(options, o => o.ErrorOnUnknownConfiguration = true);

            var environment = new ConfigurationBuilder()
                .AddEnvironmentVariables(EnvironmentPrefix)
                .AddInMemoryCollection(overrides ?? new Dictionary<string, string?>())
                .Build();
            environment.Bind(options, o => o.ErrorOnUnknownConfiguration = true);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidConfigurationException([ex.InnerException?.Message ?? ex.Message]);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidConfigurationException([$"{FileName} could not be read: {ex.InnerException?.Message ?? ex.Message}"]);
        }
        catch (FormatException ex)
        {
            throw new InvalidConfigurationException([$"{FileName} is not valid JSON: {ex.Message}"]);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidConfigurationException([$"{FileName} is not valid JSON: {ex.Message}"]);
        }

        return options;
    }
}
