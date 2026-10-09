using PCHealthMonitor.Application.Analysis;
using PCHealthMonitor.Application.Configuration;
using PCHealthMonitor.Desktop.Infrastructure;
using PCHealthMonitor.Domain.Health;
using PCHealthMonitor.Domain.Metrics;
using PCHealthMonitor.Tests.Fakes;

namespace PCHealthMonitor.Tests.Desktop;

public class PtBrTests
{
    private const ulong Gb = TestData.Gb;

    [Theory]
    [InlineData("OK", "OK")]
    [InlineData("WARNING", "ALERTA")]
    [InlineData("CRITICAL", "CRÍTICO")]
    [InlineData("UNKNOWN", "DESCONHECIDO")]
    [InlineData("HEALTHY", "SAUDÁVEL")]
    [InlineData("ATTENTION", "ATENÇÃO")]
    public void Status_labels_are_translated(string label, string expected) => Assert.Equal(expected, PtBr.Status(label));

    [Fact]
    public void Numbers_use_decimal_comma_and_portuguese_words()
    {
        Assert.Equal("93,6%", PtBr.Percent(93.6));
        Assert.Equal("5,4 GB", PtBr.Bytes((ulong)(5.4 * Gb)));
        Assert.Equal("Indisponível", PtBr.Percent(null));
        Assert.Equal("41,1 °C", PtBr.Celsius(41.1));
        Assert.Equal("8 dias 14 horas 32 minutos", PtBr.Uptime(new TimeSpan(8, 14, 32, 0)));
        Assert.Equal("1 dia 1 hora 1 minuto", PtBr.Uptime(new TimeSpan(1, 1, 1, 0)));
        Assert.Equal("+7,0 pontos percentuais", PtBr.PercentagePoints(7));
        Assert.Equal("-3,5 pontos percentuais", PtBr.PercentagePoints(-3.5));
    }

    [Fact]
    public void Analyzer_messages_are_translated_with_values_preserved()
    {
        var text = PtBr.Message("Memory usage is 93.6% (5.4 GB of 5.7 GB in use, 391.3 MB available), at or above the critical threshold of 92%.");

        Assert.Equal("Uso de memória em 93,6% (5,4 GB de 5,7 GB em uso, 391,3 MB disponíveis), igual ou acima do limite crítico de 92%.", text);
    }

    [Fact]
    public void Process_names_with_dots_are_not_changed()
    {
        var text = PtBr.Message("Largest memory consumer: Microsoft.VisualStudio.Code.ServiceHost (PID 17100) with 87.8 MB working set.");

        Assert.Equal("Maior consumo de memória: Microsoft.VisualStudio.Code.ServiceHost (PID 17100) com 87,8 MB de working set.", text);
    }

    [Fact]
    public void Unknown_text_is_shown_unchanged()
    {
        Assert.Equal("Something new.", PtBr.Message("Something new."));
        Assert.Equal(string.Empty, PtBr.Message(null));
    }

    /// <summary>Generates every analyzer message template; all of them must have a pt-BR translation.</summary>
    [Fact]
    public void Every_analyzer_message_has_a_translation()
    {
        var analyzer = new HealthAnalyzer(new ThresholdOptions());
        var messages = new List<string>();

        foreach (var (cpu, memory, disk, temperature, throttling) in new (double, double, double, double, bool)[]
                 {
                     (80, 85, 85, 88, true),   // warnings
                     (95, 95, 95, 99, false),  // criticals
                     (30, 40, 40, 40, false),  // healthy
                 })
        {
            var snapshot = new HealthSnapshot(
                FakeSystemInfoCollector.Default with { Uptime = TimeSpan.FromDays(45) },
                new CpuMetrics(new CpuInfo("CPU", 8), new SampleStatistics(10, 95, cpu, 5), TimeSpan.FromSeconds(1)),
                new MemoryMetrics(16 * Gb, (ulong)(16 * Gb * (100 - memory) / 100)),
                new StorageSnapshot([new DriveMetrics("C:", "NTFS", null, 500 * Gb, (ulong)(500 * Gb * (100 - disk) / 100))], ["D:"]),
                new TemperatureReading("ACPI", [new TemperatureZone("TZ01", temperature, throttling)], null),
                TestData.Processes(),
                [new CollectionIssue(HealthComponent.Cpu, "CPU usage could not be collected."),
                 new CollectionIssue(HealthComponent.Memory, "Memory information could not be collected.")]);
            messages.AddRange(analyzer.Analyze(snapshot).Messages.Select(m => m.Text));
        }

        // Unavailable metrics, incomplete and inconclusive diagnoses.
        messages.AddRange(analyzer.Analyze(HealthSnapshot.Empty).Messages.Select(m => m.Text));
        messages.AddRange(analyzer.Analyze(new HealthSnapshot(null, null, new MemoryMetrics(16 * Gb, 8 * Gb), null, null, null, [])).Messages.Select(m => m.Text));

        // Texts from other shared layers that the window can show.
        messages.AddRange(
        [
            "Collecting system information...",
            "Sampling CPU (5 samples, 1 s each)...",
            "Collecting memory, storage and temperature...",
            "The scan could not be saved to the history database. See the log file for details.",
            "The report could not be written. See the log file for details.",
            "Windows did not allow reading thermal zone data on this computer.",
            "Windows does not expose any thermal zone on this computer.",
            "Thermal zone values reported by the firmware are not plausible.",
            "The history database could not be opened or created at 'C:\\data\\pchealth.db'.",
        ]);
        messages.AddRange(InvalidConfigurationErrors());

        var untranslated = messages.Distinct().Where(m => !PtBr.IsKnown(m)).ToList();
        Assert.True(untranslated.Count == 0, "Untranslated:\n" + string.Join("\n", untranslated));
        Assert.True(messages.Distinct().Count() > 25);
    }

    private static IEnumerable<string> InvalidConfigurationErrors()
    {
        var options = new PcHealthOptions();
        options.Thresholds.CpuWarning = 95;
        options.Thresholds.MemoryCritical = 150;
        options.Scan.DurationSeconds = 2;
        options.Scan.SampleIntervalSeconds = 5;
        options.Monitor.RefreshIntervalSeconds = 0;
        options.Paths.LogsDirectory = string.Empty;
        return options.Validate();
    }
}
