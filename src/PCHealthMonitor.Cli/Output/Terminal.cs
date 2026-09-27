using PCHealthMonitor.Domain.Health;

namespace PCHealthMonitor.Cli.Output;

/// <summary>Console writer with optional colors (disabled when output is redirected, with --no-color or NO_COLOR).</summary>
public sealed class Terminal(TextWriter output, bool useColor)
{
    public const string Rule = "================================================================";
    public const string ThinRule = "----------------------------------------------------------------";

    public TextWriter Out { get; } = output;

    public bool UseColor { get; } = useColor;

    public static Terminal CreateConsole(bool noColorRequested)
    {
        var color = !noColorRequested
            && !Console.IsOutputRedirected
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        return new Terminal(Console.Out, color);
    }

    public void Line(string text = "") => Out.WriteLine(text);

    public void Write(string text) => Out.Write(text);

    public void Header(string title)
    {
        Line(Rule);
        Line(title);
        Line(Rule);
    }

    public void Section(string title)
    {
        Line();
        Colored(title, ConsoleColor.Cyan);
        Line();
    }

    public void Field(string label, string? value, int width = 20) =>
        Line($"  {label.PadRight(width)}{value ?? "Unavailable"}");

    public void Status(HealthStatus status) => Colored(status.ToLabel(), ColorOf(status));

    public void StatusPadded(HealthStatus status, int width) =>
        Colored(status.ToLabel().PadRight(width), ColorOf(status));

    public void Overall(OverallHealth health) => Colored(health.ToLabel(), health switch
    {
        OverallHealth.Healthy => ConsoleColor.Green,
        OverallHealth.Attention => ConsoleColor.Yellow,
        _ => ConsoleColor.Red,
    });

    public void Severity(MessageSeverity severity) => Colored($"[{severity.ToLabel()}]", severity switch
    {
        MessageSeverity.Critical => ConsoleColor.Red,
        MessageSeverity.Warning => ConsoleColor.Yellow,
        _ => ConsoleColor.Gray,
    });

    public void Error(string message) => Colored(message, ConsoleColor.Red);

    public void Colored(string text, ConsoleColor color)
    {
        if (!UseColor)
        {
            Out.Write(text);
            return;
        }

        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Out.Write(text);
        Console.ForegroundColor = previous;
    }

    private static ConsoleColor ColorOf(HealthStatus status) => status switch
    {
        HealthStatus.Ok => ConsoleColor.Green,
        HealthStatus.Warning => ConsoleColor.Yellow,
        HealthStatus.Critical => ConsoleColor.Red,
        _ => ConsoleColor.DarkGray,
    };
}
