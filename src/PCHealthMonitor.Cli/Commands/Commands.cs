using System.Globalization;
using PCHealthMonitor.Application.Formatting;
using PCHealthMonitor.Application.History;
using PCHealthMonitor.Application.Monitoring;
using PCHealthMonitor.Application.Scanning;
using PCHealthMonitor.Cli.CommandLine;
using PCHealthMonitor.Infrastructure.Composition;
using PCHealthMonitor.Cli.Output;
using PCHealthMonitor.Domain.Health;

namespace PCHealthMonitor.Cli.Commands;

public static class ExitCodes
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int Usage = 2;
    public const int Cancelled = 130;
}

public static class StatusCommand
{
    public static async Task<int> RunAsync(AppServices services, Terminal terminal, CancellationToken cancellationToken)
    {
        var result = await services.CreateHealthCheck()
            .QuickStatusAsync(TimeSpan.FromSeconds(services.Options.Scan.StatusCpuSampleSeconds), cancellationToken);
        new HealthRenderer(terminal).RenderStatus(result);
        terminal.Line();
        terminal.Line("Quick check (single CPU sample). Run 'pchealth scan' for a full diagnosis saved to history.");
        return ExitCodes.Success;
    }
}

/// <summary>"scan" (full scan saved to history, optional --report) and "report" (full scan + JSON/TXT report).</summary>
public static class ScanCommand
{
    public static async Task<int> RunAsync(ParsedArguments args, AppServices services, Terminal terminal, bool reportCommand, CancellationToken cancellationToken)
    {
        var options = services.Options;
        var settings = new ScanSettings(
            TimeSpan.FromSeconds(options.Scan.DurationSeconds),
            TimeSpan.FromSeconds(options.Scan.SampleIntervalSeconds),
            options.Processes.TopCount);
        var request = new ScanWorkflowRequest(
            settings,
            SaveToHistory: !args.HasFlag("no-save"),
            WriteReport: reportCommand || args.HasFlag("report"));

        terminal.Line($"Running full scan ({Display.Number(options.Scan.DurationSeconds)} s)... Press Ctrl+C to cancel.");
        var outcome = await services.CreateScanWorkflow().RunAsync(request, new StderrProgress(), cancellationToken);
        terminal.Line();

        var renderer = new HealthRenderer(terminal);
        if (reportCommand)
        {
            renderer.RenderStatus(outcome.Result);
        }
        else
        {
            renderer.RenderScan(outcome.Result);
        }

        if (outcome.Comparison is { } comparison)
        {
            renderer.RenderComparison(comparison);
        }

        terminal.Line();
        if (outcome.SavedScan is { } saved)
        {
            terminal.Line($"Saved to history as scan #{saved.Id} ({services.Database.DatabasePath}).");
            if (outcome.Comparison is null)
            {
                terminal.Line("No previous scan of this machine to compare with.");
            }
        }
        else if (outcome.HistoryError is { } historyError)
        {
            terminal.Error(historyError);
            terminal.Line();
        }
        else
        {
            terminal.Line("Not saved to history (--no-save).");
        }

        if (outcome.Report is { } report)
        {
            terminal.Line($"Report (JSON): {report.JsonPath}");
            if (report.TextPath is not null)
            {
                terminal.Line($"Report (TXT):  {report.TextPath}");
            }
        }
        else if (outcome.ReportError is { } reportError)
        {
            terminal.Error(reportError);
            terminal.Line();
        }

        return outcome.HistoryError is null && outcome.ReportError is null ? ExitCodes.Success : ExitCodes.Failure;
    }

    /// <summary>Progress goes to stderr immediately, so redirected stdout contains only the result.</summary>
    private sealed class StderrProgress : IProgress<string>
    {
        public void Report(string value) => Console.Error.WriteLine(value);
    }
}

public static class MonitorCommand
{
    public static async Task<int> RunAsync(ParsedArguments args, AppServices services, Terminal terminal, CancellationToken cancellationToken)
    {
        var refresh = TimeSpan.FromSeconds(services.Options.Monitor.RefreshIntervalSeconds);
        var maxTicks = args.GetInt("count");
        if (maxTicks is < 1)
        {
            throw new UsageException("Option --count must be at least 1.");
        }

        // Redraw in place on an interactive console; one line per refresh when output is redirected.
        var interactive = !Console.IsOutputRedirected;
        if (!interactive)
        {
            terminal.Line($"Monitor started (refresh {Display.Number(refresh.TotalSeconds)} s). Nothing is saved to history. Press Ctrl+C to stop.");
        }
        else
        {
            terminal.Line($"Starting monitor (refresh {Display.Number(refresh.TotalSeconds)} s)...");
        }

        var ticks = await services.CreateMonitor().RunAsync(
            refresh,
            (tick, _) =>
            {
                if (interactive)
                {
                    RenderBlock(terminal, tick, refresh);
                }
                else
                {
                    RenderLine(terminal, tick);
                }

                return Task.CompletedTask;
            },
            maxTicks,
            cancellationToken);

        terminal.Line();
        terminal.Line($"Monitor stopped after {ticks} refresh(es). Nothing was saved to history.");
        return ExitCodes.Success;
    }

    private static void RenderBlock(Terminal terminal, MonitorTick tick, TimeSpan refresh)
    {
        try
        {
            Console.Clear();
        }
        catch (IOException)
        {
            // No real console attached; fall back to appending.
        }

        terminal.Header("PC HEALTH MONITOR - LIVE");
        terminal.Field("Refresh:", $"{Display.Number(refresh.TotalSeconds)} seconds");
        terminal.Field("Updated:", Display.LocalDateTime(tick.Timestamp));
        terminal.Field("Uptime:", Display.Uptime(tick.Uptime));
        terminal.Line();
        Metric(terminal, "CPU:", Display.Percent(tick.CpuPercent), tick.CpuStatus);
        var memory = tick.Memory is { TotalBytes: > 0 } m
            ? $"{Display.Percent(m.UsagePercent)} ({Display.Bytes(m.UsedBytes)} of {Display.Bytes(m.TotalBytes)})"
            : "Unavailable";
        Metric(terminal, "Memory:", memory, tick.MemoryStatus);
        foreach (var drive in tick.Drives)
        {
            Metric(terminal, $"{drive.Name}", Display.Percent(drive.UsagePercent), drive.Status);
        }

        terminal.Line();
        terminal.Line("Live view only - nothing is saved to history. Press Ctrl+C to stop.");
    }

    private static void Metric(Terminal terminal, string label, string value, HealthStatus status)
    {
        terminal.Write($"  {label,-20}{value,-32}");
        terminal.Status(status);
        terminal.Line();
    }

    private static void RenderLine(Terminal terminal, MonitorTick tick)
    {
        var drives = string.Join("  ", tick.Drives.Select(d => $"{d.Name} {Display.Percent(d.UsagePercent)} [{d.Status.ToLabel()}]"));
        var memory = tick.Memory is { TotalBytes: > 0 } m ? Display.Percent(m.UsagePercent) : "Unavailable";
        terminal.Line(string.Create(CultureInfo.InvariantCulture,
            $"{tick.Timestamp.ToLocalTime():HH:mm:ss}  CPU {Display.Percent(tick.CpuPercent)} [{tick.CpuStatus.ToLabel()}]  Memory {memory} [{tick.MemoryStatus.ToLabel()}]  {drives}  Uptime {Display.UptimeShort(tick.Uptime)}"));
    }
}

public static class ProcessesCommand
{
    public static async Task<int> RunAsync(AppServices services, Terminal terminal, CancellationToken cancellationToken)
    {
        var options = services.Options.Processes;
        var window = TimeSpan.FromSeconds(options.CpuSampleSeconds);
        terminal.Line($"Measuring process CPU usage over {Display.Number(window.TotalSeconds)} s...");
        var summary = await services.CreateProcessService().GetTopProcessesAsync(window, options.TopCount, cancellationToken);

        terminal.Header($"PC HEALTH MONITOR - TOP {options.TopCount} PROCESSES");
        new HealthRenderer(terminal).RenderProcessTables(summary);
        terminal.Line();
        terminal.Line("Read-only view: no process is changed or terminated by this tool.");
        return ExitCodes.Success;
    }
}

public static class HistoryCommand
{
    public static async Task<int> RunAsync(ParsedArguments args, AppServices services, Terminal terminal, CancellationToken cancellationToken)
    {
        var history = services.CreateHistory();
        var sub = args.Positionals.Count > 0 ? args.Positionals[0].ToLowerInvariant() : "list";
        return sub switch
        {
            "list" => await ListAsync(args, services, history, terminal, cancellationToken),
            "show" => await ShowAsync(args, history, terminal, cancellationToken),
            "compare" => await CompareAsync(args, history, terminal, cancellationToken),
            _ => throw new UsageException($"Unknown history action '{sub}'. Use: history [list] | history show <id> | history compare [<id> <id>]."),
        };
    }

    private static async Task<int> ListAsync(ParsedArguments args, AppServices services, HistoryService history, Terminal terminal, CancellationToken ct)
    {
        var limit = args.GetInt("limit") ?? services.Options.History.DefaultListLimit;
        if (limit < 1)
        {
            throw new UsageException("Option --limit must be at least 1.");
        }

        var scans = await history.ListAsync(limit, ct);
        if (scans.Count == 0)
        {
            terminal.Line("No scans in history yet. Run 'pchealth scan' to create one.");
            return ExitCodes.Success;
        }

        terminal.Line($"{"ID",-6}{"DATE",-19}{"HOST",-16}{"CPU AVG",9}{"CPU PEAK",10}{"MEMORY",9}   {"DISK MAX",-14}HEALTH");
        terminal.Line(new string('-', 96));
        foreach (var scan in scans)
        {
            var disk = scan.Drives.Count == 0
                ? "n/a"
                : scan.Drives.MaxBy(d => d.UsagePercent) is { } d ? $"{d.Name} {Display.Percent(d.UsagePercent)}" : "n/a";
            terminal.Write($"{("#" + scan.Id.ToString(CultureInfo.InvariantCulture)),-6}{Display.LocalDateTimeShort(scan.StartedAt),-19}{Truncate(scan.Hostname, 15),-16}{Display.Percent(scan.CpuAverage),9}{Display.Percent(scan.CpuPeak),10}{Display.Percent(scan.MemoryUsagePercent),9}   {disk,-14}");
            terminal.Overall(scan.OverallHealth);
            terminal.Line(scan.IsIncomplete ? " *" : string.Empty);
        }

        terminal.Line();
        terminal.Line($"{scans.Count} scan(s) shown, most recent first. * = incomplete data. Details: pchealth history show <id>");
        return ExitCodes.Success;
    }

    private static async Task<int> ShowAsync(ParsedArguments args, HistoryService history, Terminal terminal, CancellationToken ct)
    {
        var id = ParseId(args, 1) ?? throw new UsageException("Usage: pchealth history show <id>");
        var scan = await history.GetAsync(id, ct);
        if (scan is null)
        {
            terminal.Error($"Scan #{id} was not found in history.");
            terminal.Line();
            return ExitCodes.Failure;
        }

        terminal.Header($"SCAN #{scan.Id}");
        terminal.Field("Machine:", scan.Hostname);
        terminal.Field("Windows:", scan.WindowsVersion);
        terminal.Field("Started:", Display.LocalDateTime(scan.StartedAt));
        terminal.Field("Finished:", Display.LocalDateTime(scan.FinishedAt));
        terminal.Field("Uptime:", scan.UptimeSeconds is { } up ? Display.Uptime(TimeSpan.FromSeconds(up)) : null);
        terminal.Line();
        StatusLine(terminal, "CPU", $"avg {Display.Percent(scan.CpuAverage)}, peak {Display.Percent(scan.CpuPeak)}, min {Display.Percent(scan.CpuMinimum)}", scan.CpuStatus);
        var memory = scan.MemoryUsagePercent is null
            ? "Unavailable"
            : $"{Display.Percent(scan.MemoryUsagePercent)} ({Display.Bytes(scan.MemoryUsedBytes)} of {Display.Bytes(scan.MemoryTotalBytes)})";
        StatusLine(terminal, "Memory", memory, scan.MemoryStatus);
        foreach (var drive in scan.Drives)
        {
            StatusLine(terminal, $"Drive {drive.Name}", $"{Display.Percent(drive.UsagePercent)} used, {Display.Bytes(drive.FreeBytes)} free of {Display.Bytes(drive.TotalBytes)}", drive.Status);
        }

        StatusLine(terminal, "Temperature", Display.Celsius(scan.TemperatureCelsius), scan.TemperatureStatus);
        terminal.Line();
        terminal.Write("Overall Health: ");
        terminal.Overall(scan.OverallHealth);
        terminal.Line(scan.IsIncomplete ? " (incomplete data)" : string.Empty);

        if (scan.Messages.Count > 0)
        {
            terminal.Line();
            terminal.Line("Messages:");
            foreach (var message in scan.Messages.OrderByDescending(m => m.Severity))
            {
                terminal.Write("  ");
                terminal.Severity(message.Severity);
                terminal.Line($" {message.Text}");
            }
        }

        if (await history.CompareWithPreviousAsync(scan, ct) is { } comparison)
        {
            new HealthRenderer(terminal).RenderComparison(comparison);
        }

        return ExitCodes.Success;
    }

    private static async Task<int> CompareAsync(ParsedArguments args, HistoryService history, Terminal terminal, CancellationToken ct)
    {
        ScanComparison? comparison;
        if (args.Positionals.Count == 3)
        {
            var first = await history.GetAsync(ParseId(args, 1)!.Value, ct);
            var second = await history.GetAsync(ParseId(args, 2)!.Value, ct);
            if (first is null || second is null)
            {
                terminal.Error("One of the scans was not found in history.");
                terminal.Line();
                return ExitCodes.Failure;
            }

            var ordered = new[] { first, second }.OrderBy(s => s.StartedAt).ThenBy(s => s.Id).ToArray();
            comparison = HistoryComparer.Compare(ordered[0], ordered[1]);
        }
        else if (args.Positionals.Count == 1)
        {
            var recent = await history.ListAsync(1, ct);
            var latest = recent.Count > 0 ? recent[0] : null;
            comparison = latest is null ? null : await history.CompareWithPreviousAsync(latest, ct);
        }
        else
        {
            throw new UsageException("Usage: pchealth history compare [<older-id> <newer-id>]");
        }

        if (comparison is null)
        {
            terminal.Line("At least two scans of the same machine are needed to compare.");
            return ExitCodes.Success;
        }

        terminal.Line($"Comparing scan #{comparison.Previous.Id} ({Display.LocalDateTimeShort(comparison.Previous.StartedAt)}) with scan #{comparison.Current.Id} ({Display.LocalDateTimeShort(comparison.Current.StartedAt)})");
        new HealthRenderer(terminal).RenderComparison(comparison);
        return ExitCodes.Success;
    }

    private static int? ParseId(ParsedArguments args, int position)
    {
        if (args.Positionals.Count <= position)
        {
            return null;
        }

        var raw = args.Positionals[position].TrimStart('#');
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id
            : throw new UsageException($"'{args.Positionals[position]}' is not a valid scan id.");
    }

    private static void StatusLine(Terminal terminal, string label, string value, HealthStatus status)
    {
        terminal.Write($"  {label,-14}");
        terminal.StatusPadded(status, 10);
        terminal.Line(value);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}
