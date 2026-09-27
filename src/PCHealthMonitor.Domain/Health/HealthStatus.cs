namespace PCHealthMonitor.Domain.Health;

/// <summary>Health state of a single component (CPU, memory, storage, temperature).</summary>
public enum HealthStatus
{
    /// <summary>Metric could not be collected, is not supported, or collection failed non-critically.</summary>
    Unknown = 0,
    Ok = 1,
    Warning = 2,
    Critical = 3,
}

/// <summary>Overall classification of the machine.</summary>
public enum OverallHealth
{
    Healthy = 0,
    Attention = 1,
    Critical = 2,
}

public enum MessageSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2,
}

public enum HealthComponent
{
    Cpu,
    Memory,
    Storage,
    Temperature,
    Processes,
    System,
}

public static class HealthLabels
{
    public static string ToLabel(this HealthStatus status) => status switch
    {
        HealthStatus.Ok => "OK",
        HealthStatus.Warning => "WARNING",
        HealthStatus.Critical => "CRITICAL",
        _ => "UNKNOWN",
    };

    public static string ToLabel(this OverallHealth health) => health switch
    {
        OverallHealth.Healthy => "HEALTHY",
        OverallHealth.Attention => "ATTENTION",
        _ => "CRITICAL",
    };

    public static string ToLabel(this MessageSeverity severity) => severity switch
    {
        MessageSeverity.Info => "INFO",
        MessageSeverity.Warning => "WARNING",
        _ => "CRITICAL",
    };

    public static string ToLabel(this HealthComponent component) => component switch
    {
        HealthComponent.Cpu => "CPU",
        HealthComponent.Memory => "Memory",
        HealthComponent.Storage => "Storage",
        HealthComponent.Temperature => "Temperature",
        HealthComponent.Processes => "Processes",
        _ => "System",
    };

    public static HealthStatus ParseStatus(string? value) => value?.ToUpperInvariant() switch
    {
        "OK" => HealthStatus.Ok,
        "WARNING" => HealthStatus.Warning,
        "CRITICAL" => HealthStatus.Critical,
        _ => HealthStatus.Unknown,
    };

    public static OverallHealth? ParseOverall(string? value) => value?.ToUpperInvariant() switch
    {
        "HEALTHY" => OverallHealth.Healthy,
        "ATTENTION" => OverallHealth.Attention,
        "CRITICAL" => OverallHealth.Critical,
        _ => null,
    };

    public static MessageSeverity ParseSeverity(string? value) => value?.ToUpperInvariant() switch
    {
        "CRITICAL" => MessageSeverity.Critical,
        "WARNING" => MessageSeverity.Warning,
        _ => MessageSeverity.Info,
    };

    public static HealthComponent ParseComponent(string? value) => value?.ToUpperInvariant() switch
    {
        "CPU" => HealthComponent.Cpu,
        "MEMORY" => HealthComponent.Memory,
        "STORAGE" => HealthComponent.Storage,
        "TEMPERATURE" => HealthComponent.Temperature,
        "PROCESSES" => HealthComponent.Processes,
        _ => HealthComponent.System,
    };
}
