namespace PCHealthMonitor.Domain.Machine;

/// <summary>Identification of the machine and operating system. Unknown values are null.</summary>
public sealed record SystemInfo(
    string Hostname,
    string? WindowsName,
    string? Edition,
    string? DisplayVersion,
    string? Build,
    string Architecture,
    TimeSpan? Uptime,
    string? CurrentUser,
    DateTimeOffset Timestamp)
{
    public string WindowsDescription
    {
        get
        {
            var parts = new[] { WindowsName, Edition, DisplayVersion }.Where(p => !string.IsNullOrWhiteSpace(p));
            var text = string.Join(' ', parts);
            return text.Length == 0 ? "Unknown" : text;
        }
    }
}
