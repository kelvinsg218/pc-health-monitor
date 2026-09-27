namespace PCHealthMonitor.Cli.Commands;

public static class HelpText
{
    public const string General = """
        PC Health Monitor - read-only health diagnostics for Windows 10/11

        Usage: pchealth <command> [options]

        Commands:
          status      Quick check of CPU, memory, storage and temperature (not saved)
          scan        Full scan: CPU sampled over a window, processes, analysis; saved to history
          monitor     Live view refreshed periodically (Ctrl+C to stop; never saved)
          processes   Top processes by memory and by CPU
          report      Full scan + JSON and TXT report in reports/
          history     List saved scans; 'history show <id>'; 'history compare [<id> <id>]'
          version     Show version information

        Global options:
          -h, --help      Show help (also: pchealth <command> --help)
          --no-color      Disable colored output

        During development run with:
          dotnet run --project ./src/PCHealthMonitor.Cli -- <command> [options]

        This tool only reads information. It never changes system settings, kills processes or deletes files.
        """;

    public static string? ForCommand(string command) => command switch
    {
        "status" => """
            pchealth status

            Quick diagnosis: one CPU measurement (Scan:StatusCpuSampleSeconds, default 1 s), memory,
            local fixed drives and temperature (if Windows exposes it). Nothing is saved.
            """,
        "scan" => """
            pchealth scan [--duration <seconds>] [--interval <seconds>] [--top <n>] [--report] [--no-save]

            Full diagnosis. CPU is sampled every --interval seconds during --duration seconds
            (defaults from appsettings.json: 5 s / 1 s) and min/avg/peak are computed. Process CPU is
            measured over the same window. The result is saved to data/pchealth.db and compared with
            the previous scan of this machine.

              --duration <s>   Sampling window (1-600)
              --interval <s>   Interval of each sample (0.25-60, <= duration)
              --top <n>        Number of processes kept per ranking
              --report         Also write reports/pchealth_<HOST>_<DATE>_<TIME>.json and .txt
              --no-save        Do not save the scan to history
            """,
        "monitor" => """
            pchealth monitor [--refresh <seconds>] [--count <n>]

            Shows CPU, memory, drives and uptime every --refresh seconds (default 2).
            Press Ctrl+C to stop. Monitor refreshes are never saved to history.

              --refresh <s>    Refresh interval (0.5-3600)
              --count <n>      Stop automatically after n refreshes
            """,
        "processes" => """
            pchealth processes [--top <n>] [--sample <seconds>]

            Top processes by memory (working set) and by CPU. CPU is measured as the process CPU time
            consumed during --sample seconds (default 1) divided by the capacity of all logical processors.
            Protected processes that deny access are shown without CPU data; nothing is changed.

              --top <n>        Number of processes per ranking (1-100, default 10)
              --sample <s>     CPU measurement window (0.25-30)
            """,
        "report" => """
            pchealth report [--duration <seconds>] [--interval <seconds>] [--top <n>] [--no-save]

            Runs a full scan, saves it to history and writes a JSON report (and a TXT version) to
            reports/pchealth_<HOST>_<YYYY-MM-DD>_<HHMMSS>.json.
            """,
        "history" => """
            pchealth history [list] [--limit <n>]
            pchealth history show <id>
            pchealth history compare [<older-id> <newer-id>]

            Lists saved full scans (most recent first), shows the details of one scan, or compares two
            scans (default: the latest scan and the previous scan of the same machine). Differences are
            shown in percentage points and describe observed change only.
            """,
        "version" => "pchealth version\n\nShows version, runtime and OS information.",
        _ => null,
    };
}
