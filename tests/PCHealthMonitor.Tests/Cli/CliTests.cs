using PCHealthMonitor.Application.Configuration;
using PCHealthMonitor.Application.Formatting;
using PCHealthMonitor.Cli;
using PCHealthMonitor.Cli.CommandLine;
using PCHealthMonitor.Cli.Output;
using PCHealthMonitor.Domain.Metrics;

namespace PCHealthMonitor.Tests.Cli;

public class CliTests
{
    [Fact]
    public void No_arguments_means_help()
    {
        Assert.Equal("help", CommandLineParser.Parse([]).Command);
        Assert.True(CommandLineParser.Parse(["--help"]).Command == "help");
    }

    [Fact]
    public void Version_aliases_are_supported()
    {
        Assert.Equal("version", CommandLineParser.Parse(["--version"]).Command);
        Assert.Equal("version", CommandLineParser.Parse(["version"]).Command);
    }

    [Fact]
    public void Scan_options_are_parsed()
    {
        var parsed = CommandLineParser.Parse(["scan", "--duration", "10", "--interval=2,5", "--report", "--no-save"]);

        Assert.Equal("scan", parsed.Command);
        Assert.Equal(10, parsed.GetDouble("duration"));
        Assert.Equal(2.5, parsed.GetDouble("interval"));
        Assert.True(parsed.HasFlag("report"));
        Assert.True(parsed.HasFlag("no-save"));
    }

    [Fact]
    public void Command_help_flag_is_detected()
    {
        var parsed = CommandLineParser.Parse(["monitor", "-h"]);

        Assert.Equal("monitor", parsed.Command);
        Assert.True(parsed.WantsHelp);
    }

    [Fact]
    public void History_positionals_are_parsed()
    {
        var parsed = CommandLineParser.Parse(["history", "show", "12"]);

        Assert.Equal(["show", "12"], parsed.Positionals);
    }

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("status", "--duration", "5")]
    [InlineData("scan", "--duration")]
    [InlineData("scan", "--report=yes")]
    [InlineData("processes", "extra")]
    public void Invalid_usage_is_rejected(params string[] args)
    {
        Assert.Throws<UsageException>(() => CommandLineParser.Parse(args));
    }

    [Fact]
    public void Non_numeric_values_are_rejected()
    {
        var parsed = CommandLineParser.Parse(["scan", "--duration", "abc"]);

        Assert.Throws<UsageException>(() => parsed.GetDouble("duration"));
    }

    [Fact]
    public void Command_line_overrides_are_validated_like_configuration()
    {
        var options = new PcHealthOptions();
        CliApplication.ApplyCommandLineOverrides(CommandLineParser.Parse(["scan", "--duration", "0"]), options);

        Assert.Contains(options.Validate(), e => e.Contains("Scan.DurationSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void Status_rendering_shows_components_overall_and_reason()
    {
        var output = new StringWriter();
        var result = TestData.Result(memoryPercent: 83);

        new HealthRenderer(new Terminal(output, useColor: false)).RenderStatus(result);

        var text = output.ToString();
        Assert.Contains("PC HEALTH MONITOR - STATUS", text, StringComparison.Ordinal);
        Assert.Contains("Overall Health: ATTENTION", text, StringComparison.Ordinal);
        Assert.Contains("Temperature   Unavailable", text, StringComparison.Ordinal);
        Assert.Contains("UNKNOWN", text, StringComparison.Ordinal);
        Assert.Contains("Memory usage is 83.0%", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_rendering_includes_processes_and_comparison()
    {
        var output = new StringWriter();
        var renderer = new HealthRenderer(new Terminal(output, useColor: false));
        var previous = TestData.Record(70, 74, TestData.BaseTime) with { Id = 1 };
        var current = TestData.Record(80, 81, TestData.BaseTime.AddDays(1)) with { Id = 2 };

        renderer.RenderScan(TestData.Result(processes: TestData.Processes()));
        renderer.RenderComparison(Application.History.HistoryComparer.Compare(previous, current));

        var text = output.ToString();
        Assert.Contains("TOP MEMORY", text, StringComparison.Ordinal);
        Assert.Contains("chrome", text, StringComparison.Ordinal);
        Assert.Contains("+10.0 percentage points", text, StringComparison.Ordinal);
        Assert.Contains("+7.0 percentage points", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 0, 5, "5 minutes")]
    [InlineData(0, 1, 0, "1 hour 0 minutes")]
    [InlineData(8, 14, 32, "8 days 14 hours 32 minutes")]
    [InlineData(1, 0, 1, "1 day 0 hours 1 minute")]
    public void Uptime_is_human_readable(int days, int hours, int minutes, string expected)
    {
        Assert.Equal(expected, Display.Uptime(new TimeSpan(days, hours, minutes, 0)));
    }

    [Fact]
    public void Sizes_use_binary_units_and_invariant_formatting()
    {
        Assert.Equal("16.0 GB", Display.Bytes(16UL * 1024 * 1024 * 1024));
        Assert.Equal("512.0 MB", Display.Bytes(512UL * 1024 * 1024));
        Assert.Equal("80.6%", Display.Percent(80.6));
        Assert.Equal("Unavailable", Display.Percent(double.NaN));
        Assert.Equal("Unavailable", Display.Bytes((long?)null));
    }

    [Fact]
    public void Memory_and_drive_percentages_are_computed_from_bytes()
    {
        var memory = new MemoryMetrics(16UL * 1024 * 1024 * 1024, 3_328_599_655);

        Assert.Equal(80.6, memory.UsagePercent, precision: 1);
        Assert.True(double.IsNaN(new MemoryMetrics(0, 0).UsagePercent));
        Assert.Equal(0UL, new DriveMetrics("X:", null, null, 10, 20).UsedBytes);
    }
}
