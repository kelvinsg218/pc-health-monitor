using PCHealthMonitor.Application.Configuration;
using PCHealthMonitor.Application.Formatting;
using PCHealthMonitor.Domain.Health;
using PCHealthMonitor.Domain.Metrics;

namespace PCHealthMonitor.Application.Analysis;

/// <summary>
/// Classifies collected metrics. Rules:
/// <list type="bullet">
/// <item>CPU: average usage over the sampling window vs. CPU thresholds (the peak is reported as evidence only).</item>
/// <item>Memory: used / usable physical memory vs. memory thresholds.</item>
/// <item>Storage: each readable local fixed drive vs. storage thresholds; the component takes the worst drive.</item>
/// <item>Temperature: firmware thermal zone (if exposed) vs. temperature thresholds; reported passive throttling is at least WARNING.</item>
/// <item>Overall: any CRITICAL → CRITICAL; else any WARNING → ATTENTION; else HEALTHY.
/// UNKNOWN never raises the overall state by itself. If no component could be evaluated the result is ATTENTION (inconclusive).</item>
/// </list>
/// Messages describe only what was measured; they never claim causes or hardware defects.
/// </summary>
public sealed class HealthAnalyzer(ThresholdOptions thresholds)
{
    private static readonly HealthComponent[] CoreComponents = [HealthComponent.Cpu, HealthComponent.Memory, HealthComponent.Storage];

    public HealthAssessment Analyze(HealthSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var messages = new List<DiagnosticMessage>();
        var drives = new List<ComponentHealth>();
        var components = new List<ComponentHealth>
        {
            AnalyzeCpu(snapshot.Cpu, messages),
            AnalyzeMemory(snapshot.Memory, messages),
            AnalyzeStorage(snapshot.Storage, drives, messages),
            AnalyzeTemperature(snapshot.Temperature, messages),
        };

        AddUptimeNote(snapshot, messages);
        AddProcessNotes(snapshot.Processes, messages);

        foreach (var issue in snapshot.Issues)
        {
            messages.Add(new DiagnosticMessage(MessageSeverity.Info, issue.Component, issue.Message));
        }

        var evaluated = components.Where(c => c.Status != HealthStatus.Unknown).ToList();
        var unknown = components.Where(c => c.Status == HealthStatus.Unknown).ToList();
        var unreadableDrives = snapshot.Storage?.UnreadableDrives.Count ?? 0;

        var isIncomplete = unknown.Count >= 2
            || unknown.Any(c => CoreComponents.Contains(c.Component))
            || unreadableDrives > 0;

        OverallHealth overall;
        if (evaluated.Count == 0)
        {
            overall = OverallHealth.Attention;
            messages.Add(new DiagnosticMessage(
                MessageSeverity.Warning,
                HealthComponent.System,
                "No health metric could be collected; the diagnosis is inconclusive."));
        }
        else if (evaluated.Any(c => c.Status == HealthStatus.Critical))
        {
            overall = OverallHealth.Critical;
        }
        else if (evaluated.Any(c => c.Status == HealthStatus.Warning))
        {
            overall = OverallHealth.Attention;
        }
        else
        {
            overall = OverallHealth.Healthy;
        }

        if (isIncomplete && evaluated.Count > 0)
        {
            var names = unknown.Select(c => c.Component.ToLabel()).ToList();
            if (unreadableDrives > 0 && !names.Contains(HealthComponent.Storage.ToLabel()))
            {
                names.Add($"{unreadableDrives} drive(s)");
            }

            messages.Add(new DiagnosticMessage(
                MessageSeverity.Info,
                HealthComponent.System,
                $"Diagnosis based on incomplete data: {string.Join(", ", names)} could not be evaluated."));
        }

        return new HealthAssessment(components, drives, overall, isIncomplete, messages);
    }

    private ComponentHealth AnalyzeCpu(CpuMetrics? cpu, List<DiagnosticMessage> messages)
    {
        if (cpu is null)
        {
            messages.Add(new(MessageSeverity.Info, HealthComponent.Cpu, "CPU usage could not be measured on this computer."));
            return new(HealthComponent.Cpu, "CPU", HealthStatus.Unknown, "Unavailable");
        }

        var usage = cpu.Usage;
        var limits = thresholds.Cpu;
        var status = limits.Classify(usage.Average);
        var evidence = usage.SampleCount > 1
            ? $"average {Display.Percent(usage.Average)}, peak {Display.Percent(usage.Peak)}, minimum {Display.Percent(usage.Minimum)} ({usage.SampleCount} samples)"
            : $"{Display.Percent(usage.Average)} (1 sample over {Display.Number(cpu.SampleInterval.TotalSeconds)} s)";

        var window = usage.SampleCount > 1 ? "Average CPU usage during the scan" : "CPU usage";
        if (status == HealthStatus.Critical)
        {
            messages.Add(new(MessageSeverity.Critical, HealthComponent.Cpu,
                $"{window} was {Display.Percent(usage.Average)}, at or above the critical threshold of {Display.Number(limits.Critical)}%."));
        }
        else if (status == HealthStatus.Warning)
        {
            messages.Add(new(MessageSeverity.Warning, HealthComponent.Cpu,
                $"{window} was {Display.Percent(usage.Average)}, above the warning threshold of {Display.Number(limits.Warning)}%."));
        }

        if (usage.SampleCount > 1 && status != HealthStatus.Critical && usage.Peak >= limits.Critical)
        {
            messages.Add(new(MessageSeverity.Info, HealthComponent.Cpu,
                $"CPU reached a peak of {Display.Percent(usage.Peak)} during the scan (average {Display.Percent(usage.Average)})."));
        }

        return new(HealthComponent.Cpu, "CPU", status, evidence);
    }

    private ComponentHealth AnalyzeMemory(MemoryMetrics? memory, List<DiagnosticMessage> messages)
    {
        if (memory is null || memory.TotalBytes == 0)
        {
            messages.Add(new(MessageSeverity.Info, HealthComponent.Memory, "Memory usage could not be read on this computer."));
            return new(HealthComponent.Memory, "Memory", HealthStatus.Unknown, "Unavailable");
        }

        var limits = thresholds.Memory;
        var usage = memory.UsagePercent;
        var status = limits.Classify(usage);
        var evidence = $"{Display.Bytes(memory.UsedBytes)} of {Display.Bytes(memory.TotalBytes)} in use, {Display.Bytes(memory.AvailableBytes)} available";

        if (status == HealthStatus.Critical)
        {
            messages.Add(new(MessageSeverity.Critical, HealthComponent.Memory,
                $"Memory usage is {Display.Percent(usage)} ({evidence}), at or above the critical threshold of {Display.Number(limits.Critical)}%."));
        }
        else if (status == HealthStatus.Warning)
        {
            messages.Add(new(MessageSeverity.Warning, HealthComponent.Memory,
                $"Memory usage is {Display.Percent(usage)} ({evidence}), above the warning threshold of {Display.Number(limits.Warning)}%."));
        }

        return new(HealthComponent.Memory, "Memory", status, evidence);
    }

    private ComponentHealth AnalyzeStorage(StorageSnapshot? storage, List<ComponentHealth> drives, List<DiagnosticMessage> messages)
    {
        if (storage is null || (storage.Drives.Count == 0 && storage.UnreadableDrives.Count == 0))
        {
            messages.Add(new(MessageSeverity.Info, HealthComponent.Storage, "No local fixed drive could be read."));
            return new(HealthComponent.Storage, "Storage", HealthStatus.Unknown, "Unavailable");
        }

        var limits = thresholds.Storage;
        foreach (var drive in storage.Drives)
        {
            var status = limits.Classify(drive.UsagePercent);
            var evidence = $"{Display.Percent(drive.UsagePercent)} used, {Display.Bytes(drive.FreeBytes)} free of {Display.Bytes(drive.TotalBytes)}";
            drives.Add(new(HealthComponent.Storage, drive.Name, status, evidence));

            if (status == HealthStatus.Critical)
            {
                messages.Add(new(MessageSeverity.Critical, HealthComponent.Storage,
                    $"Drive {drive.Name} is {Display.Percent(drive.UsagePercent)} full ({Display.Bytes(drive.FreeBytes)} free of {Display.Bytes(drive.TotalBytes)}), at or above the critical threshold of {Display.Number(limits.Critical)}%."));
            }
            else if (status == HealthStatus.Warning)
            {
                messages.Add(new(MessageSeverity.Warning, HealthComponent.Storage,
                    $"Drive {drive.Name} has less free space than recommended: {Display.Percent(drive.UsagePercent)} used ({Display.Bytes(drive.FreeBytes)} free), above the warning threshold of {Display.Number(limits.Warning)}%."));
            }
        }

        foreach (var name in storage.UnreadableDrives)
        {
            drives.Add(new(HealthComponent.Storage, name, HealthStatus.Unknown, "Unavailable"));
            messages.Add(new(MessageSeverity.Info, HealthComponent.Storage,
                $"Drive {name} could not be read (it may be locked, not ready or inaccessible)."));
        }

        var known = drives.Where(d => d.Status != HealthStatus.Unknown).ToList();
        var worst = known.Count == 0 ? HealthStatus.Unknown : known.Max(d => d.Status);
        var summary = known.Count == 0
            ? "Unavailable"
            : string.Join(", ", storage.Drives.Select(d => $"{d.Name} {Display.Percent(d.UsagePercent)}"));
        return new(HealthComponent.Storage, "Storage", worst, summary);
    }

    private ComponentHealth AnalyzeTemperature(TemperatureReading? reading, List<DiagnosticMessage> messages)
    {
        if (reading is null || !reading.IsAvailable)
        {
            messages.Add(new(MessageSeverity.Info, HealthComponent.Temperature,
                "Temperature could not be obtained on this computer."));
            return new(HealthComponent.Temperature, "Temperature", HealthStatus.Unknown, "Unavailable");
        }

        var limits = thresholds.Temperature;
        var hottest = reading.Zones.MaxBy(z => z.Celsius)!;
        var status = limits.Classify(hottest.Celsius);
        var evidence = $"{Display.Celsius(hottest.Celsius)} (ACPI thermal zone {hottest.Name}, not necessarily the CPU core)";

        if (status == HealthStatus.Critical)
        {
            messages.Add(new(MessageSeverity.Critical, HealthComponent.Temperature,
                $"Thermal zone {hottest.Name} reports {Display.Celsius(hottest.Celsius)}, at or above the critical threshold of {Display.Number(limits.Critical)} °C."));
        }
        else if (status == HealthStatus.Warning)
        {
            messages.Add(new(MessageSeverity.Warning, HealthComponent.Temperature,
                $"Thermal zone {hottest.Name} reports {Display.Celsius(hottest.Celsius)}, above the warning threshold of {Display.Number(limits.Warning)} °C."));
        }

        if (reading.ThrottlingReported)
        {
            var zones = string.Join(", ", reading.Zones.Where(z => z.PassiveThrottling == true).Select(z => z.Name));
            messages.Add(new(MessageSeverity.Warning, HealthComponent.Temperature,
                $"Windows reports passive thermal throttling on zone(s) {zones}: processor performance is being limited by the thermal policy."));
            if (status < HealthStatus.Warning)
            {
                status = HealthStatus.Warning;
            }
        }

        return new(HealthComponent.Temperature, "Temperature", status, evidence);
    }

    private void AddUptimeNote(HealthSnapshot snapshot, List<DiagnosticMessage> messages)
    {
        var uptime = snapshot.System?.Uptime;
        if (uptime is null || thresholds.UptimeNoticeDays <= 0)
        {
            return;
        }

        if (uptime.Value.TotalDays >= thresholds.UptimeNoticeDays)
        {
            messages.Add(new(MessageSeverity.Info, HealthComponent.System,
                $"System has been running for {Display.Uptime(uptime)} since the last restart (informational)."));
        }
    }

    private static void AddProcessNotes(ProcessSummary? processes, List<DiagnosticMessage> messages)
    {
        if (processes is null)
        {
            return;
        }

        if (processes.TopByMemory.Count > 0 && processes.TopByMemory[0] is { } topMemory)
        {
            messages.Add(new(MessageSeverity.Info, HealthComponent.Processes,
                $"Largest memory consumer: {topMemory.Name} (PID {topMemory.Pid}) with {Display.Bytes(topMemory.WorkingSetBytes)} working set."));
        }

        if (processes.TopByCpu.Count > 0 && processes.TopByCpu[0] is { CpuPercent: > 0 } topCpu)
        {
            messages.Add(new(MessageSeverity.Info, HealthComponent.Processes,
                $"Highest CPU consumer during the sampling window: {topCpu.Name} (PID {topCpu.Pid}) at {Display.Percent(topCpu.CpuPercent)} of total CPU."));
        }

        if (processes.CpuUnavailableCount > 0)
        {
            messages.Add(new(MessageSeverity.Info, HealthComponent.Processes,
                $"CPU usage could not be measured for {processes.CpuUnavailableCount} of {processes.TotalProcesses} processes (processes started or ended during the sampling window, or access was denied)."));
        }
    }
}
