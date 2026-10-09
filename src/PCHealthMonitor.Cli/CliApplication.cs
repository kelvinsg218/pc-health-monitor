using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PCHealthMonitor.Application.Configuration;
using PCHealthMonitor.Cli.CommandLine;
using PCHealthMonitor.Cli.Commands;
using PCHealthMonitor.Infrastructure.Composition;
using PCHealthMonitor.Cli.Output;
using PCHealthMonitor.Infrastructure.Persistence;

namespace PCHealthMonitor.Cli;

/// <summary>Parses arguments, loads configuration, dispatches commands and maps errors to friendly messages.</summary>
public static class CliApplication
{
    public static string Version
    {
        get
        {
            var informational = typeof(CliApplication).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? informational[..plus] : informational;
        }
    }

    public static async Task<int> RunAsync(string[] args)
    {
        ParsedArguments parsed;
        try
        {
            parsed = CommandLineParser.Parse(args);
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.Error.WriteLine("Run 'pchealth --help' for usage.");
            return ExitCodes.Usage;
        }

        var terminal = Terminal.CreateConsole(parsed.HasFlag("no-color"));
        var command = parsed.Command!;

        if (command == "help" || parsed.WantsHelp)
        {
            var topic = command == "help" ? (parsed.Positionals.Count > 0 ? parsed.Positionals[0] : null) : command;
            terminal.Line(topic is null ? HelpText.General : HelpText.ForCommand(topic.ToLowerInvariant()) ?? HelpText.General);
            return ExitCodes.Success;
        }

        if (command == "version")
        {
            terminal.Line($"pchealth {Version}");
            terminal.Line($"PC Health Monitor - runtime .NET {Environment.Version}, {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
            return ExitCodes.Success;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("PC Health Monitor supports Windows 10 and Windows 11 only.");
            return ExitCodes.Failure;
        }

        PcHealthOptions options;
        try
        {
            options = ConfigurationLoader.Load(AppContext.BaseDirectory);
            ApplyCommandLineOverrides(parsed, options);
            options.EnsureValid();
        }
        catch (InvalidConfigurationException ex)
        {
            Console.Error.WriteLine("Invalid configuration:");
            foreach (var error in ex.Errors)
            {
                Console.Error.WriteLine($"  - {error}");
            }

            return ExitCodes.Usage;
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return ExitCodes.Usage;
        }

        using var services = new AppServices(options, Environment.CurrentDirectory, Version);
        var logger = services.LoggerFactory.CreateLogger("PCHealthMonitor.Cli");
        logger.LogInformation("pchealth {Version} started: command '{Command}'.", Version, command);

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            if (!cancellation.IsCancellationRequested)
            {
                e.Cancel = true; // first Ctrl+C: stop cleanly
                cancellation.Cancel();
            }
        };
        Console.CancelKeyPress += onCancel;

        try
        {
            return command switch
            {
                "status" => await StatusCommand.RunAsync(services, terminal, cancellation.Token),
                "scan" => await ScanCommand.RunAsync(parsed, services, terminal, reportCommand: false, cancellation.Token),
                "report" => await ScanCommand.RunAsync(parsed, services, terminal, reportCommand: true, cancellation.Token),
                "monitor" => await MonitorCommand.RunAsync(parsed, services, terminal, cancellation.Token),
                "processes" => await ProcessesCommand.RunAsync(services, terminal, cancellation.Token),
                "history" => await HistoryCommand.RunAsync(parsed, services, terminal, cancellation.Token),
                _ => throw new UsageException($"Unknown command '{command}'."),
            };
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            terminal.Line();
            terminal.Line("Cancelled. Nothing was saved.");
            logger.LogInformation("Command '{Command}' cancelled by the user.", command);
            return ExitCodes.Cancelled;
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return ExitCodes.Usage;
        }
        catch (PersistenceException ex)
        {
            terminal.Error(ex.Message);
            terminal.Line();
            terminal.Line($"Technical details were written to {services.LogProvider.CurrentLogFile}");
            return ExitCodes.Failure;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Unexpected error while running '{Command}'.", command);
            terminal.Error($"Unexpected error: {ex.Message}");
            terminal.Line();
            terminal.Line($"Technical details were written to {services.LogProvider.CurrentLogFile}");
            return ExitCodes.Failure;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary>Command-line options override appsettings.json values for this run only.</summary>
    internal static void ApplyCommandLineOverrides(ParsedArguments parsed, PcHealthOptions options)
    {
        if (parsed.GetDouble("duration") is { } duration)
        {
            options.Scan.DurationSeconds = duration;
        }

        if (parsed.GetDouble("interval") is { } interval)
        {
            options.Scan.SampleIntervalSeconds = interval;
        }

        if (parsed.GetInt("top") is { } top)
        {
            options.Processes.TopCount = top;
        }

        if (parsed.GetDouble("sample") is { } sample)
        {
            options.Processes.CpuSampleSeconds = sample;
        }

        if (parsed.GetDouble("refresh") is { } refresh)
        {
            options.Monitor.RefreshIntervalSeconds = refresh;
        }
    }
}
