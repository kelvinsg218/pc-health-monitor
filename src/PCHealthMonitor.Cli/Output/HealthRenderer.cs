using System.Globalization;
using PCHealthMonitor.Application.Formatting;
using PCHealthMonitor.Application.History;
using PCHealthMonitor.Application.Scanning;
using PCHealthMonitor.Domain.Health;
using PCHealthMonitor.Domain.Metrics;

namespace PCHealthMonitor.Cli.Output;

/// <summary>Renders check results, comparisons and process tables. No diagnostic logic lives here.</summary>
public sealed class HealthRenderer(Terminal terminal)
{
    public void RenderStatus(HealthCheckResult result)
    {
        terminal.Header("PC HEALTH MONITOR - STATUS");
        RenderMachine(result);

        terminal.Line();
        terminal.Line($"  {"COMPONENT",-14}{"VALUE",-12}{"STATUS",-10}EVIDENCE");
        var snapshot = result.Snapshot;
        var assessment = result.Assessment;

        Row("CPU", snapshot.Cpu is { } cpu ? Display.Percent(cpu.Usage.Average) : "Unavailable", assessment, HealthComponent.Cpu);
        Row("Memory", snapshot.Memory is { TotalBytes: > 0 } memory ? Display.Percent(memory.UsagePercent) : "Unavailable", assessment, HealthComponent.Memory);
        RenderDriveRows(assessment, snapshot.Storage);
        Row("Temperature", snapshot.Temperature?.MaxCelsius is { } t ? Display.Celsius(t) : "Unavailable", assessment, HealthComponent.Temperature);

        RenderOverall(result.Assessment, includeInfo: true);
    }

    public void RenderScan(HealthCheckResult result)
    {
        terminal.Header("PC HEALTH MONITOR - FULL SCAN");
        RenderMachine(result);
        var snapshot = result.Snapshot;
        var assessment = result.Assessment;

        terminal.Section("CPU");
        if (snapshot.Cpu is { } cpu)
        {
            terminal.Field("Model:", cpu.Info.Name);
            terminal.Field("Logical processors:", cpu.Info.LogicalProcessorCount.ToString(CultureInfo.InvariantCulture));
            terminal.Field("Average:", Display.Percent(cpu.Usage.Average));
            terminal.Field("Peak:", Display.Percent(cpu.Usage.Peak));
            terminal.Field("Minimum:", Display.Percent(cpu.Usage.Minimum));
            terminal.Field("Samples:", $"{cpu.Usage.SampleCount} x {Display.Number(cpu.SampleInterval.TotalSeconds)} s");
        }
        else
        {
            terminal.Field("Usage:", "Unavailable");
        }

        StatusField(assessment.StatusOf(HealthComponent.Cpu));

        terminal.Section("MEMORY");
        if (snapshot.Memory is { TotalBytes: > 0 } memory)
        {
            terminal.Field("Total (usable):", Display.Bytes(memory.TotalBytes));
            if (memory.InstalledBytes is { } installed && installed != memory.TotalBytes)
            {
                terminal.Field("Installed:", $"{Display.Bytes(installed)} (part is reserved by hardware/firmware)");
            }

            terminal.Field("Used:", Display.Bytes(memory.UsedBytes));
            terminal.Field("Available:", Display.Bytes(memory.AvailableBytes));
            terminal.Field("Usage:", Display.Percent(memory.UsagePercent));
        }
        else
        {
            terminal.Field("Usage:", "Unavailable");
        }

        StatusField(assessment.StatusOf(HealthComponent.Memory));

        terminal.Section("STORAGE (local fixed drives)");
        if (snapshot.Storage is { } storage)
        {
            foreach (var drive in storage.Drives)
            {
                terminal.Line($"  {drive.Name} {drive.FileSystem ?? string.Empty}{(drive.Label is null ? string.Empty : $" \"{drive.Label}\"")}");
                terminal.Field("  Total:", Display.Bytes(drive.TotalBytes));
                terminal.Field("  Used:", Display.Bytes(drive.UsedBytes));
                terminal.Field("  Available:", Display.Bytes(drive.FreeBytes));
                terminal.Field("  Usage:", Display.Percent(drive.UsagePercent));
                terminal.Write($"  {"  Status:",-20}");
                terminal.Status(assessment.DriveStatus(drive.Name) ?? HealthStatus.Unknown);
                terminal.Line();
            }

            foreach (var name in storage.UnreadableDrives)
            {
                terminal.Field(name, "Unavailable (drive not ready or inaccessible)");
            }
        }
        else
        {
            terminal.Field("Drives:", "Unavailable");
        }

        terminal.Section("TEMPERATURE");
        if (snapshot.Temperature is { IsAvailable: true } temperature)
        {
            foreach (var zone in temperature.Zones)
            {
                terminal.Field($"Zone {zone.Name}:", Display.Celsius(zone.Celsius));
            }

            terminal.Field("Source:", temperature.Source);
            terminal.Field("Note:", "firmware thermal zone, not necessarily the CPU core sensor");
        }
        else
        {
            terminal.Field("Temperature:", "Unavailable");
            terminal.Field("Reason:", snapshot.Temperature?.UnavailableReason);
        }

        StatusField(assessment.StatusOf(HealthComponent.Temperature));

        if (snapshot.Processes is { } processes)
        {
            terminal.Section($"TOP PROCESSES (CPU measured over the {Display.Number(processes.CpuSampleWindow.TotalSeconds, "0.0")} s scan window)");
            RenderProcessTables(processes, Math.Min(5, Math.Max(processes.TopByMemory.Count, processes.TopByCpu.Count)));
        }

        RenderOverall(assessment, includeInfo: true);
    }

    public void RenderProcessTables(ProcessSummary summary, int? limit = null)
    {
        var memory = limit is { } l1 ? summary.TopByMemory.Take(l1) : summary.TopByMemory;
        var cpu = limit is { } l2 ? summary.TopByCpu.Take(l2) : summary.TopByCpu;

        terminal.Line();
        terminal.Line("  TOP MEMORY (working set)");
        terminal.Line($"  {"PID",7}  {"PROCESS",-34}{"MEMORY",12}");
        foreach (var p in memory)
        {
            terminal.Line($"  {p.Pid,7}  {Truncate(p.Name, 33),-34}{Display.Bytes(p.WorkingSetBytes),12}");
        }

        terminal.Line();
        terminal.Line("  TOP CPU (% of total CPU capacity)");
        terminal.Line($"  {"PID",7}  {"PROCESS",-34}{"CPU",12}");
        var any = false;
        foreach (var p in cpu)
        {
            any = true;
            terminal.Line($"  {p.Pid,7}  {Truncate(p.Name, 33),-34}{Display.Percent(p.CpuPercent),12}");
        }

        if (!any)
        {
            terminal.Line("  (no process CPU data could be measured)");
        }

        terminal.Line();
        terminal.Line(summary.CpuUnavailableCount == 0
            ? $"  {summary.TotalProcesses} processes inspected; CPU measured for all of them."
            : $"  {summary.TotalProcesses} processes inspected; CPU unavailable for {summary.CpuUnavailableCount} " +
              "(process started or ended during sampling, or access was denied).");
    }

    public void RenderComparison(ScanComparison comparison)
    {
        terminal.Section($"CHANGES SINCE PREVIOUS SCAN #{comparison.Previous.Id} ({Display.LocalDateTimeShort(comparison.Previous.StartedAt)})");
        terminal.Line("  Observed change between the two scans (not a prediction).");
        terminal.Line($"  {"METRIC",-22}{"PREVIOUS",10}{"CURRENT",10}   CHANGE");
        foreach (var change in comparison.Changes)
        {
            var delta = change.DeltaPoints is { } d ? Display.PercentagePoints(d) : "n/a";
            terminal.Line($"  {change.Metric,-22}{Display.Percent(change.Previous),10}{Display.Percent(change.Current),10}   {delta}");
        }

        terminal.Line($"  {"Overall health",-22}{comparison.Previous.OverallHealth.ToLabel(),10}{comparison.Current.OverallHealth.ToLabel(),10}   {(comparison.OverallChanged ? "changed" : "unchanged")}");
    }

    public void RenderOverall(HealthAssessment assessment, bool includeInfo)
    {
        terminal.Line();
        terminal.Line(Terminal.ThinRule);
        terminal.Write("Overall Health: ");
        terminal.Overall(assessment.Overall);
        if (assessment.IsIncomplete)
        {
            terminal.Write(" (incomplete data)");
        }

        terminal.Line();
        var reasons = assessment.Reasons.ToList();
        if (reasons.Count > 0)
        {
            terminal.Line();
            terminal.Line("Reason:");
            foreach (var message in reasons)
            {
                terminal.Write("  ");
                terminal.Severity(message.Severity);
                terminal.Line($" {message.Text}");
            }
        }

        if (includeInfo)
        {
            var notes = assessment.Messages.Where(m => m.Severity == MessageSeverity.Info).ToList();
            if (notes.Count > 0)
            {
                terminal.Line();
                terminal.Line("Notes:");
                foreach (var message in notes)
                {
                    terminal.Line($"  - {message.Text}");
                }
            }
        }
    }

    private void RenderMachine(HealthCheckResult result)
    {
        var system = result.Snapshot.System;
        terminal.Field("Machine:", result.Hostname, 14);
        if (system is not null)
        {
            var build = system.Build is null ? string.Empty : $" (build {system.Build}, {system.Architecture})";
            terminal.Field("Windows:", system.WindowsDescription + build, 14);
            terminal.Field("Uptime:", Display.Uptime(system.Uptime), 14);
        }
        else
        {
            terminal.Field("Windows:", "Unavailable", 14);
        }

        terminal.Field("Checked at:", Display.LocalDateTime(result.StartedAt), 14);
    }

    private void RenderDriveRows(HealthAssessment assessment, StorageSnapshot? storage)
    {
        if (storage is null || assessment.Drives.Count == 0)
        {
            Row("Storage", "Unavailable", assessment, HealthComponent.Storage);
            return;
        }

        foreach (var drive in assessment.Drives)
        {
            var metrics = storage.Drives.FirstOrDefault(d => d.Name == drive.Name);
            var value = metrics is null ? "Unavailable" : Display.Percent(metrics.UsagePercent);
            terminal.Write($"  {("Storage " + drive.Name),-14}{value,-12}");
            terminal.StatusPadded(drive.Status, 10);
            terminal.Line(drive.Evidence);
        }
    }

    private void Row(string label, string value, HealthAssessment assessment, HealthComponent component)
    {
        var health = assessment.Components.First(c => c.Component == component);
        terminal.Write($"  {label,-14}{value,-12}");
        terminal.StatusPadded(health.Status, 10);
        terminal.Line(health.Evidence);
    }

    private void StatusField(HealthStatus status)
    {
        terminal.Write($"  {"Status:",-20}");
        terminal.Status(status);
        terminal.Line();
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}
