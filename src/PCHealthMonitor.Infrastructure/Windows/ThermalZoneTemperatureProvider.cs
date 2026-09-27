using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PCHealthMonitor.Application.Abstractions;
using PCHealthMonitor.Domain.Metrics;

namespace PCHealthMonitor.Infrastructure.Windows;

/// <summary>
/// Reads ACPI thermal zones from the "Thermal Zone Information" performance counters exposed
/// through WMI (Win32_PerfRawData_Counters_ThermalZoneInformation). This source works without
/// administrator rights, unlike MSAcpi_ThermalZoneTemperature.
/// A thermal zone is defined by the firmware and is NOT necessarily the CPU core sensor.
/// Implausible values are discarded; when nothing plausible is exposed the reading is Unavailable.
/// </summary>
public sealed class ThermalZoneTemperatureProvider(ILogger<ThermalZoneTemperatureProvider> logger) : ITemperatureProvider
{
    public const string SourceName = "ACPI thermal zone (Windows performance counters)";

    private const string Query = "SELECT * FROM Win32_PerfRawData_Counters_ThermalZoneInformation";

    public TemperatureReading Read()
    {
        var raw = new List<RawThermalZone>();
        try
        {
            var options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(5), ReturnImmediately = false };
            using var searcher = new ManagementObjectSearcher(new ManagementScope(@"root\cimv2"), new ObjectQuery(Query), options);
            using var results = searcher.Get();
            foreach (var item in results)
            {
                using (item)
                {
                    raw.Add(new RawThermalZone(
                        item["Name"] as string ?? "zone",
                        TryGetUInt(item, "HighPrecisionTemperature"),
                        TryGetUInt(item, "Temperature"),
                        TryGetUInt(item, "PercentPassiveLimit")));
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or TimeoutException)
        {
            logger.LogInformation(ex, "Thermal zone counters could not be read.");
            return TemperatureReading.Unavailable("Windows did not allow reading thermal zone data on this computer.");
        }

        return Interpret(raw);
    }

    /// <summary>Converts raw counter values (tenths of Kelvin / Kelvin) and discards implausible ones.</summary>
    internal static TemperatureReading Interpret(IReadOnlyList<RawThermalZone> zones)
    {
        if (zones.Count == 0)
        {
            return TemperatureReading.Unavailable("Windows does not expose any thermal zone on this computer.");
        }

        var valid = new List<TemperatureZone>();
        foreach (var zone in zones)
        {
            double? kelvin = zone.HighPrecisionTenthsKelvin is > 0 ? zone.HighPrecisionTenthsKelvin / 10.0
                : zone.Kelvin is > 0 ? zone.Kelvin
                : null;
            if (kelvin is null)
            {
                continue;
            }

            var celsius = kelvin.Value - 273.15;
            if (celsius is < MinPlausibleCelsius or > MaxPlausibleCelsius)
            {
                continue;
            }

            bool? throttling = zone.PercentPassiveLimit is { } limit ? limit < 100 : null;
            valid.Add(new TemperatureZone(ShortName(zone.Name), Math.Round(celsius, 1), throttling));
        }

        return valid.Count == 0
            ? TemperatureReading.Unavailable("Thermal zone values reported by the firmware are not plausible.")
            : new TemperatureReading(SourceName, valid, null);
    }

    // Values outside this range are almost always placeholders (0 K, 0 °C...) or broken firmware tables.
    internal const double MinPlausibleCelsius = 1;
    internal const double MaxPlausibleCelsius = 125;

    private static string ShortName(string name)
    {
        // "\_TZ.TZ01" → "TZ01"
        var index = name.LastIndexOf('.');
        return index >= 0 && index < name.Length - 1 ? name[(index + 1)..] : name.TrimStart('\\');
    }

    private static uint? TryGetUInt(ManagementBaseObject item, string property)
    {
        try
        {
            return item[property] is { } value ? Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture) : null;
        }
        catch (ManagementException)
        {
            return null; // property not present on this Windows version
        }
    }
}

internal sealed record RawThermalZone(string Name, uint? HighPrecisionTenthsKelvin, uint? Kelvin, uint? PercentPassiveLimit);
