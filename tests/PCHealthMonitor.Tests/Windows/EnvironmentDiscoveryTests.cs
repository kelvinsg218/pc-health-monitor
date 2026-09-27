using PCHealthMonitor.Domain.Machine;
using PCHealthMonitor.Infrastructure.Windows;

namespace PCHealthMonitor.Tests.Windows;

/// <summary>
/// Reproduces behaviors found while validating on a real Windows 11 machine, using the raw
/// values observed there (no dependency on the machine running the tests).
/// </summary>
public class EnvironmentDiscoveryTests
{
    [Fact]
    public void Windows_11_is_detected_by_build_even_when_product_name_says_windows_10()
    {
        // Observed: ProductName = "Windows 10 Home Single Language", CurrentBuild = 26200 (Windows 11 25H2).
        const string productName = "Windows 10 Home Single Language";

        Assert.Equal("Windows 11", WindowsVersionResolver.ResolveName(26200, productName));
        Assert.Equal("Home Single Language", WindowsVersionResolver.ResolveEdition(productName, "CoreSingleLanguage"));
    }

    [Theory]
    [InlineData(22000, "Windows 11")]
    [InlineData(21999, "Windows 10")]
    [InlineData(19045, "Windows 10")]
    [InlineData(10240, "Windows 10")]
    public void Build_number_decides_between_windows_10_and_11(int build, string expected)
    {
        Assert.Equal(expected, WindowsVersionResolver.ResolveName(build, "Windows 10 Pro"));
    }

    [Fact]
    public void Missing_information_is_not_invented()
    {
        Assert.Null(WindowsVersionResolver.ResolveName(null, null));
        Assert.Null(WindowsVersionResolver.ResolveEdition(null, null));
        Assert.Equal("Professional", WindowsVersionResolver.ResolveEdition(null, "Professional"));
        Assert.Equal("Windows 8.1", WindowsVersionResolver.ResolveName(9600, "Windows 8.1 Pro"));
    }

    [Fact]
    public void Thermal_zone_high_precision_value_is_tenths_of_kelvin()
    {
        // Observed: \_TZ.TZ01 HighPrecisionTemperature = 3092 (309.2 K), Temperature = 309, PercentPassiveLimit = 100.
        var reading = ThermalZoneTemperatureProvider.Interpret([new RawThermalZone(@"\_TZ.TZ01", 3092, 309, 100)]);

        var zone = Assert.Single(reading.Zones);
        Assert.Equal("TZ01", zone.Name);
        Assert.InRange(zone.Celsius, 36.0, 36.1); // 309.2 K − 273.15
        Assert.False(zone.PassiveThrottling);
        Assert.Equal(ThermalZoneTemperatureProvider.SourceName, reading.Source);
    }

    [Fact]
    public void Thermal_zone_falls_back_to_whole_kelvin_when_high_precision_is_missing()
    {
        var reading = ThermalZoneTemperatureProvider.Interpret([new RawThermalZone("TZ00", null, 318, null)]);

        Assert.Equal(44.9, Assert.Single(reading.Zones).Celsius, precision: 1);
        Assert.Null(reading.Zones[0].PassiveThrottling);
    }

    [Theory]
    [InlineData(0u)]     // 0 K placeholder
    [InlineData(2732u)]  // exactly 0 °C placeholder
    [InlineData(5000u)]  // 226.9 °C: not plausible
    public void Implausible_thermal_values_are_discarded_not_shown(uint tenthsKelvin)
    {
        var reading = ThermalZoneTemperatureProvider.Interpret([new RawThermalZone("TZ01", tenthsKelvin, tenthsKelvin / 10, 100)]);

        Assert.False(reading.IsAvailable);
        Assert.Null(reading.MaxCelsius);
        Assert.NotNull(reading.UnavailableReason);
    }

    [Fact]
    public void No_thermal_zone_means_unavailable()
    {
        var reading = ThermalZoneTemperatureProvider.Interpret([]);

        Assert.False(reading.IsAvailable);
        Assert.Contains("does not expose", reading.UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Passive_limit_below_100_means_throttling()
    {
        var reading = ThermalZoneTemperatureProvider.Interpret([new RawThermalZone("TZ01", 3632, 363, 70)]);

        Assert.True(reading.ThrottlingReported);
    }
}
