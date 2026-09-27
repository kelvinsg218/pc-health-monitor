using System.Globalization;

namespace PCHealthMonitor.Cli.CommandLine;

/// <summary>Parsed command line: command name, positional arguments and --options.</summary>
public sealed class ParsedArguments
{
    public ParsedArguments(string? command, IReadOnlyList<string> positionals, IReadOnlyDictionary<string, string?> options)
    {
        Command = command;
        Positionals = positionals;
        Options = options;
    }

    public string? Command { get; }

    public IReadOnlyList<string> Positionals { get; }

    public IReadOnlyDictionary<string, string?> Options { get; }

    public bool HasFlag(string name) => Options.ContainsKey(name);

    public bool WantsHelp => HasFlag("help");

    /// <summary>Parses a positive number option ("2.5" or "2,5"). Returns null when absent.</summary>
    public double? GetDouble(string name)
    {
        if (!Options.TryGetValue(name, out var raw) || raw is null)
        {
            return null;
        }

        var normalized = raw.Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
        {
            throw new UsageException($"Option --{name} expects a number (got '{raw}').");
        }

        return value;
    }

    public int? GetInt(string name)
    {
        if (!Options.TryGetValue(name, out var raw) || raw is null)
        {
            return null;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new UsageException($"Option --{name} expects an integer (got '{raw}').");
        }

        return value;
    }
}

/// <summary>Invalid command line usage (shown to the user with a hint to --help).</summary>
public sealed class UsageException(string message) : Exception(message);

/// <summary>Options accepted by one command. Valued options take an argument; flags do not.</summary>
public sealed record CommandSpec(string Name, string[] ValuedOptions, string[] Flags, int MaxPositionals);

public static class CommandLineParser
{
    private static readonly string[] GlobalFlags = ["help", "no-color"];

    public static IReadOnlyDictionary<string, CommandSpec> Commands { get; } = new Dictionary<string, CommandSpec>(StringComparer.OrdinalIgnoreCase)
    {
        ["status"] = new("status", [], [], 0),
        ["scan"] = new("scan", ["duration", "interval", "top"], ["report", "no-save"], 0),
        ["monitor"] = new("monitor", ["refresh", "count"], [], 0),
        ["processes"] = new("processes", ["top", "sample"], [], 0),
        ["report"] = new("report", ["duration", "interval", "top"], ["no-save"], 0),
        ["history"] = new("history", ["limit"], [], 3),
        ["version"] = new("version", [], [], 0),
        ["help"] = new("help", [], [], 1),
    };

    public static ParsedArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? command = null;
        var positionals = new List<string>();
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // Command is the first non-option argument.
        var index = 0;
        var pending = new List<string>();
        for (; index < args.Count; index++)
        {
            if (!args[index].StartsWith('-'))
            {
                command = args[index].ToLowerInvariant();
                index++;
                break;
            }

            pending.Add(args[index]);
        }

        // Top-level aliases: "pchealth --help", "pchealth --version".
        if (command is null)
        {
            if (pending.Any(a => a is "--version" or "-v"))
            {
                return new ParsedArguments("version", [], options);
            }

            command = "help";
        }

        if (!Commands.TryGetValue(command, out var spec))
        {
            throw new UsageException($"Unknown command '{command}'.");
        }

        var remaining = pending.Concat(args.Skip(index)).ToList();
        for (var i = 0; i < remaining.Count; i++)
        {
            var arg = remaining[i];
            if (!arg.StartsWith('-') || arg == "-")
            {
                positionals.Add(arg);
                continue;
            }

            var name = arg is "-h" or "/?" ? "help" : arg.TrimStart('-');
            string? inlineValue = null;
            var equals = name.IndexOf('=', StringComparison.Ordinal);
            if (equals >= 0)
            {
                inlineValue = name[(equals + 1)..];
                name = name[..equals];
            }

            name = name.ToLowerInvariant();
            if (GlobalFlags.Contains(name) || spec.Flags.Contains(name))
            {
                if (inlineValue is not null)
                {
                    throw new UsageException($"Option --{name} does not take a value.");
                }

                options[name] = null;
            }
            else if (spec.ValuedOptions.Contains(name))
            {
                var value = inlineValue;
                if (value is null)
                {
                    if (i + 1 >= remaining.Count)
                    {
                        throw new UsageException($"Option --{name} requires a value.");
                    }

                    value = remaining[++i];
                }

                options[name] = value;
            }
            else
            {
                throw new UsageException($"Unknown option '{arg}' for command '{spec.Name}'.");
            }
        }

        if (positionals.Count > spec.MaxPositionals)
        {
            throw new UsageException($"Unexpected argument '{positionals[spec.MaxPositionals]}' for command '{spec.Name}'.");
        }

        return new ParsedArguments(spec.Name, positionals, options);
    }
}
