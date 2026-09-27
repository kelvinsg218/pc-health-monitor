namespace PCHealthMonitor.Domain.Health;

/// <summary>Status of one component (or drive) plus a short, evidence-based summary.</summary>
public sealed record ComponentHealth(HealthComponent Component, string Name, HealthStatus Status, string Evidence);

/// <summary>A diagnostic message derived only from collected data.</summary>
public sealed record DiagnosticMessage(MessageSeverity Severity, HealthComponent Component, string Text);

/// <summary>Result of the analysis: per-component statuses, overall classification and messages.</summary>
public sealed record HealthAssessment(
    IReadOnlyList<ComponentHealth> Components,
    IReadOnlyList<ComponentHealth> Drives,
    OverallHealth Overall,
    bool IsIncomplete,
    IReadOnlyList<DiagnosticMessage> Messages)
{
    public HealthStatus StatusOf(HealthComponent component) =>
        Components.FirstOrDefault(c => c.Component == component)?.Status ?? HealthStatus.Unknown;

    public HealthStatus? DriveStatus(string driveName) =>
        Drives.FirstOrDefault(d => string.Equals(d.Name, driveName, StringComparison.OrdinalIgnoreCase))?.Status;

    /// <summary>Messages that explain the overall classification (criticals first, then warnings).</summary>
    public IEnumerable<DiagnosticMessage> Reasons =>
        Messages.Where(m => m.Severity >= MessageSeverity.Warning).OrderByDescending(m => m.Severity);
}
