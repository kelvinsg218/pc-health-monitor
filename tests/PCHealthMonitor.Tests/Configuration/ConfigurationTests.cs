using PCHealthMonitor.Application.Configuration;
using PCHealthMonitor.Cli.Composition;
using PCHealthMonitor.Domain.Health;

namespace PCHealthMonitor.Tests.Configuration;

public class ConfigurationTests
{
    [Fact]
    public void Defaults_are_valid_and_match_product_defaults()
    {
        var options = new PcHealthOptions();

        Assert.Empty(options.Validate());
        Assert.Equal(new ThresholdPair(75, 90), options.Thresholds.Cpu);
        Assert.Equal(new ThresholdPair(80, 92), options.Thresholds.Memory);
        Assert.Equal(new ThresholdPair(80, 90), options.Thresholds.Storage);
        Assert.Equal(5, options.Scan.DurationSeconds);
        Assert.Equal(1, options.Scan.SampleIntervalSeconds);
        Assert.Equal(2, options.Monitor.RefreshIntervalSeconds);
        Assert.Equal(10, options.Processes.TopCount);
        Assert.Equal(365, options.History.RetentionDays);
    }

    [Fact]
    public void Custom_thresholds_are_accepted()
    {
        var options = new PcHealthOptions();
        options.Thresholds.CpuWarning = 60;
        options.Thresholds.CpuCritical = 85;

        Assert.Empty(options.Validate());
        Assert.Equal(HealthStatus.Warning, options.Thresholds.Cpu.Classify(70));
    }

    [Fact]
    public void Warning_greater_than_critical_is_rejected()
    {
        var options = new PcHealthOptions();
        options.Thresholds.MemoryWarning = 95;
        options.Thresholds.MemoryCritical = 90;

        var error = Assert.Single(options.Validate());
        Assert.Contains("Thresholds.Memory", error, StringComparison.Ordinal);
        Assert.Contains("cannot be greater than critical", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    public void Out_of_range_threshold_is_rejected(double value)
    {
        var options = new PcHealthOptions();
        options.Thresholds.StorageWarning = value;

        Assert.Contains(options.Validate(), e => e.Contains("Thresholds.Storage: warning", StringComparison.Ordinal));
    }

    [Fact]
    public void Critical_above_100_percent_is_rejected()
    {
        var options = new PcHealthOptions();
        options.Thresholds.CpuCritical = 150;

        Assert.Contains(options.Validate(), e => e.Contains("Thresholds.Cpu: critical", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(601)]
    public void Invalid_scan_duration_is_rejected(double duration)
    {
        var options = new PcHealthOptions();
        options.Scan.DurationSeconds = duration;

        Assert.Contains(options.Validate(), e => e.Contains("Scan.DurationSeconds", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.1)]
    [InlineData(-2)]
    public void Invalid_sampling_interval_is_rejected(double interval)
    {
        var options = new PcHealthOptions();
        options.Scan.SampleIntervalSeconds = interval;

        Assert.Contains(options.Validate(), e => e.Contains("Scan.SampleIntervalSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void Sampling_interval_longer_than_duration_is_rejected()
    {
        var options = new PcHealthOptions();
        options.Scan.DurationSeconds = 2;
        options.Scan.SampleIntervalSeconds = 5;

        Assert.Contains(options.Validate(), e => e.Contains("cannot be greater than Scan.DurationSeconds", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.1)]
    [InlineData(4000)]
    public void Invalid_monitor_refresh_is_rejected(double refresh)
    {
        var options = new PcHealthOptions();
        options.Monitor.RefreshIntervalSeconds = refresh;

        Assert.Contains(options.Validate(), e => e.Contains("Monitor.RefreshIntervalSeconds", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Invalid_top_count_is_rejected(int top)
    {
        var options = new PcHealthOptions();
        options.Processes.TopCount = top;

        Assert.Contains(options.Validate(), e => e.Contains("Processes.TopCount", StringComparison.Ordinal));
    }

    [Fact]
    public void Negative_retention_is_rejected_and_zero_means_keep_forever()
    {
        var options = new PcHealthOptions();
        options.History.RetentionDays = 0;
        Assert.Empty(options.Validate());

        options.History.RetentionDays = -1;
        Assert.Contains(options.Validate(), e => e.Contains("History.RetentionDays", StringComparison.Ordinal));
    }

    [Fact]
    public void EnsureValid_throws_with_all_errors()
    {
        var options = new PcHealthOptions();
        options.Thresholds.CpuWarning = 95;
        options.Scan.DurationSeconds = 0;

        var ex = Assert.Throws<InvalidConfigurationException>(options.EnsureValid);
        Assert.True(ex.Errors.Count >= 2);
    }

    [Fact]
    public void Loader_uses_defaults_when_file_is_missing()
    {
        using var dir = new TempDirectory();

        var options = ConfigurationLoader.Load(dir.Path);

        Assert.Equal(75, options.Thresholds.CpuWarning);
        Assert.Empty(options.Validate());
    }

    [Fact]
    public void Loader_reads_values_from_appsettings()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "appsettings.json"),
            """{ "PcHealth": { "Thresholds": { "CpuWarning": 60 }, "Scan": { "DurationSeconds": 10 } } }""");

        var options = ConfigurationLoader.Load(dir.Path);

        Assert.Equal(60, options.Thresholds.CpuWarning);
        Assert.Equal(90, options.Thresholds.CpuCritical);
        Assert.Equal(10, options.Scan.DurationSeconds);
    }

    [Fact]
    public void Loader_applies_overrides_after_the_file()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "appsettings.json"), """{ "PcHealth": { "Scan": { "DurationSeconds": 10 } } }""");

        var options = ConfigurationLoader.Load(dir.Path, new Dictionary<string, string?> { ["Scan:DurationSeconds"] = "3" });

        Assert.Equal(3, options.Scan.DurationSeconds);
    }

    [Fact]
    public void Loader_rejects_unknown_keys()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "appsettings.json"), """{ "PcHealth": { "Thresholds": { "CpuWarnng": 60 } } }""");

        var ex = Assert.Throws<InvalidConfigurationException>(() => ConfigurationLoader.Load(dir.Path));
        Assert.Contains("CpuWarnng", ex.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Loader_rejects_non_numeric_values()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "appsettings.json"), """{ "PcHealth": { "Thresholds": { "CpuWarning": "high" } } }""");

        Assert.Throws<InvalidConfigurationException>(() => ConfigurationLoader.Load(dir.Path));
    }

    [Fact]
    public void Loader_rejects_malformed_json()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "appsettings.json"), """{ "PcHealth": { """);

        Assert.Throws<InvalidConfigurationException>(() => ConfigurationLoader.Load(dir.Path));
    }

    [Fact]
    public void Shipped_appsettings_is_valid_and_equals_defaults()
    {
        var options = ConfigurationLoader.Load(AppContext.BaseDirectory);

        Assert.Empty(options.Validate());
        Assert.Equal(new PcHealthOptions().Thresholds.Cpu, options.Thresholds.Cpu);
        Assert.Equal(new PcHealthOptions().Thresholds.Memory, options.Thresholds.Memory);
        Assert.Equal(new PcHealthOptions().Thresholds.Storage, options.Thresholds.Storage);
    }
}

public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pchealth-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
