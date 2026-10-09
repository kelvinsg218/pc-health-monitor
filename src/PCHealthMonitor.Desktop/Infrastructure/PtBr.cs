using System.Globalization;
using System.Text.RegularExpressions;
using PCHealthMonitor.Application.Formatting;

namespace PCHealthMonitor.Desktop.Infrastructure;

/// <summary>
/// Brazilian Portuguese texts for the desktop window. The shared layers (analyzer, CLI, JSON reports)
/// stay in English; this class converts what the window displays: status labels, component names,
/// numbers (decimal comma), dates and each diagnostic message template. Unknown text is shown unchanged.
/// </summary>
public static partial class PtBr
{
    public const string Unavailable = "Indisponível";

    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    public static string Status(string? label) => label?.ToUpperInvariant() switch
    {
        "OK" => "OK",
        "WARNING" => "ALERTA",
        "CRITICAL" => "CRÍTICO",
        "UNKNOWN" => "DESCONHECIDO",
        "HEALTHY" => "SAUDÁVEL",
        "ATTENTION" => "ATENÇÃO",
        "INFO" => "INFO",
        null => string.Empty,
        _ => label,
    };

    public static string Component(string? label) => label switch
    {
        "Memory" => "Memória",
        "Storage" => "Armazenamento",
        "Temperature" => "Temperatura",
        "Processes" => "Processos",
        "System" => "Sistema",
        null => string.Empty,
        _ => label,
    };

    // ---- Formatting (same values as Display, with decimal comma and Portuguese words) ----

    public static string Num(string text) => text.Replace('.', ',');

    public static string Number(double value, string format = "0.#") => value.ToString(format, Culture);

    public static string Percent(double? value) =>
        value is null || !double.IsFinite(value.Value) ? Unavailable : Num(Display.Percent(value));

    public static string Bytes(ulong bytes) => Num(Display.Bytes(bytes));

    public static string Bytes(long? bytes) => bytes is null or < 0 ? Unavailable : Bytes((ulong)bytes.Value);

    public static string Celsius(double? value) => value is null ? Unavailable : Num(Display.Celsius(value));

    public static string DateTime(DateTimeOffset value) => value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", Culture);

    public static string DateTimeShort(DateTimeOffset value) => value.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Culture);

    /// <summary>"8 dias 14 horas 32 minutos".</summary>
    public static string Uptime(TimeSpan? uptime)
    {
        if (uptime is null || uptime.Value < TimeSpan.Zero)
        {
            return Unavailable;
        }

        var v = uptime.Value;
        var parts = new List<string>();
        if (v.Days > 0)
        {
            parts.Add(v.Days == 1 ? "1 dia" : $"{v.Days} dias");
        }

        if (v.Days > 0 || v.Hours > 0)
        {
            parts.Add(v.Hours == 1 ? "1 hora" : $"{v.Hours} horas");
        }

        parts.Add(v.Minutes == 1 ? "1 minuto" : $"{v.Minutes} minutos");
        return string.Join(' ', parts);
    }

    /// <summary>"+7,0 pontos percentuais".</summary>
    public static string PercentagePoints(double delta)
    {
        var rounded = Math.Round(delta, 1);
        if (rounded == 0)
        {
            return "sem mudança (0,0 ponto percentual)";
        }

        var sign = rounded > 0 ? "+" : "-";
        return sign + Math.Abs(rounded).ToString("0.0", Culture) + " pontos percentuais";
    }

    // ---- Diagnostic messages, progress and errors produced by the shared layers ----

    private static readonly Dictionary<string, string> Exact = new(StringComparer.Ordinal)
    {
        ["No health metric could be collected; the diagnosis is inconclusive."] = "Nenhuma métrica de saúde pôde ser coletada; o diagnóstico é inconclusivo.",
        ["CPU usage could not be measured on this computer."] = "Não foi possível medir o uso de CPU neste computador.",
        ["Memory usage could not be read on this computer."] = "Não foi possível ler o uso de memória neste computador.",
        ["No local fixed drive could be read."] = "Nenhuma unidade fixa local pôde ser lida.",
        ["Temperature could not be obtained on this computer."] = "Não foi possível obter a temperatura deste computador.",
        ["Collecting system information..."] = "Coletando informações do sistema...",
        ["Collecting memory, storage and temperature..."] = "Coletando memória, armazenamento e temperatura...",
        ["The scan could not be saved to the history database. See the log file for details."] = "Não foi possível salvar a verificação no histórico. Veja o arquivo de log para detalhes.",
        ["The report could not be written. See the log file for details."] = "Não foi possível gravar o relatório. Veja o arquivo de log para detalhes.",
        ["Windows did not allow reading thermal zone data on this computer."] = "O Windows não permitiu ler os dados de zona térmica neste computador.",
        ["Windows does not expose any thermal zone on this computer."] = "O Windows não expõe nenhuma zona térmica neste computador.",
        ["Thermal zone values reported by the firmware are not plausible."] = "Os valores de zona térmica informados pelo firmware não são plausíveis.",
        ["Temperature provider failed."] = "Falha ao ler a temperatura.",
        ["Temperature could not be obtained."] = "Não foi possível obter a temperatura.",
    };

    private static readonly Dictionary<string, string> CollectedItems = new(StringComparer.Ordinal)
    {
        ["CPU usage"] = "o uso de CPU",
        ["System information"] = "as informações do sistema",
        ["Process list"] = "a lista de processos",
        ["Process summary"] = "o resumo de processos",
        ["Memory information"] = "as informações de memória",
        ["Storage information"] = "as informações de armazenamento",
        ["Temperature"] = "a temperatura",
    };

    private const string P = @"([\d.]+%)";               // percentage, e.g. 93.6%
    private const string B = @"([\d.]+ (?:B|KB|MB|GB|TB))"; // size, e.g. 5.4 GB
    private const string N = @"([\d.]+)";                // plain number

    private static readonly (Regex Pattern, Func<Match, string> Translate)[] Templates =
    [
        (Rx(@"^Diagnosis based on incomplete data: (.+) could not be evaluated\.$"),
            m => $"Diagnóstico baseado em dados incompletos: não foi possível avaliar {List(m.Groups[1].Value)}."),
        (Rx($@"^(Average CPU usage during the scan|CPU usage) was {P}, at or above the critical threshold of {N}%\.$"),
            m => $"{CpuWindow(m)} foi de {G(m, 2)}, igual ou acima do limite crítico de {G(m, 3)}%."),
        (Rx($@"^(Average CPU usage during the scan|CPU usage) was {P}, above the warning threshold of {N}%\.$"),
            m => $"{CpuWindow(m)} foi de {G(m, 2)}, acima do limite de alerta de {G(m, 3)}%."),
        (Rx($@"^CPU reached a peak of {P} during the scan \(average {P}\)\.$"),
            m => $"A CPU atingiu pico de {G(m, 1)} durante a verificação (média de {G(m, 2)})."),
        (Rx($@"^Memory usage is {P} \({B} of {B} in use, {B} available\), at or above the critical threshold of {N}%\.$"),
            m => $"Uso de memória em {G(m, 1)} ({G(m, 2)} de {G(m, 3)} em uso, {G(m, 4)} disponíveis), igual ou acima do limite crítico de {G(m, 5)}%."),
        (Rx($@"^Memory usage is {P} \({B} of {B} in use, {B} available\), above the warning threshold of {N}%\.$"),
            m => $"Uso de memória em {G(m, 1)} ({G(m, 2)} de {G(m, 3)} em uso, {G(m, 4)} disponíveis), acima do limite de alerta de {G(m, 5)}%."),
        (Rx($@"^Drive (\S+) is {P} full \({B} free of {B}\), at or above the critical threshold of {N}%\.$"),
            m => $"A unidade {m.Groups[1].Value} está {G(m, 2)} cheia ({G(m, 3)} livres de {G(m, 4)}), igual ou acima do limite crítico de {G(m, 5)}%."),
        (Rx($@"^Drive (\S+) has less free space than recommended: {P} used \({B} free\), above the warning threshold of {N}%\.$"),
            m => $"A unidade {m.Groups[1].Value} tem menos espaço livre que o recomendado: {G(m, 2)} em uso ({G(m, 3)} livres), acima do limite de alerta de {G(m, 4)}%."),
        (Rx(@"^Drive (\S+) could not be read \(it may be locked, not ready or inaccessible\)\.$"),
            m => $"Não foi possível ler a unidade {m.Groups[1].Value} (pode estar bloqueada, não pronta ou inacessível)."),
        (Rx($@"^Thermal zone (\S+) reports ([\d.]+ °C), at or above the critical threshold of {N} °C\.$"),
            m => $"A zona térmica {m.Groups[1].Value} informa {G(m, 2)}, igual ou acima do limite crítico de {G(m, 3)} °C."),
        (Rx($@"^Thermal zone (\S+) reports ([\d.]+ °C), above the warning threshold of {N} °C\.$"),
            m => $"A zona térmica {m.Groups[1].Value} informa {G(m, 2)}, acima do limite de alerta de {G(m, 3)} °C."),
        (Rx(@"^Windows reports passive thermal throttling on zone\(s\) (.+): processor performance is being limited by the thermal policy\.$"),
            m => $"O Windows informa limitação térmica passiva na(s) zona(s) {m.Groups[1].Value}: o desempenho do processador está sendo limitado pela política térmica."),
        (Rx(@"^System has been running for (.+) since the last restart \(informational\)\.$"),
            m => $"O sistema está ligado há {UptimeText(m.Groups[1].Value)} desde a última reinicialização (informativo)."),
        (Rx($@"^Largest memory consumer: (.+) \(PID (\d+)\) with {B} working set\.$"),
            m => $"Maior consumo de memória: {m.Groups[1].Value} (PID {m.Groups[2].Value}) com {G(m, 3)} de working set."),
        (Rx($@"^Highest CPU consumer during the sampling window: (.+) \(PID (\d+)\) at {P} of total CPU\.$"),
            m => $"Maior consumo de CPU durante a amostragem: {m.Groups[1].Value} (PID {m.Groups[2].Value}) com {G(m, 3)} da CPU total."),
        (Rx(@"^CPU usage could not be measured for (\d+) of (\d+) processes \(processes started or ended during the sampling window, or access was denied\)\.$"),
            m => $"Não foi possível medir o uso de CPU de {m.Groups[1].Value} de {m.Groups[2].Value} processos (processos iniciados ou encerrados durante a amostragem, ou acesso negado)."),
        (Rx(@"^(.+) could not be collected\.$"),
            m => CollectedItems.TryGetValue(m.Groups[1].Value, out var item) ? $"Não foi possível coletar {item}." : m.Value),
        (Rx($@"^Sampling CPU \((\d+) samples, {N} s each\)\.\.\.$"),
            m => $"Amostrando a CPU ({m.Groups[1].Value} amostras de {G(m, 2)} s cada)..."),
        (Rx(@"^The history database could not be opened or created at '(.+)'\.$"),
            m => $"Não foi possível abrir ou criar o banco de histórico em '{m.Groups[1].Value}'."),
        // Configuration validation (PcHealthOptions.Validate); setting names stay as written in appsettings.json.
        (Rx(@"^(\S+): (warning|critical) threshold must be greater than (\S+) and at most (\S+) \(was (\S+)\)\.$"),
            m => $"{m.Groups[1].Value}: o limite {(m.Groups[2].Value == "warning" ? "de alerta" : "crítico")} deve ser maior que {G(m, 3)} e no máximo {G(m, 4)} (valor: {G(m, 5)})."),
        (Rx(@"^(\S+): warning threshold \((\S+)\) cannot be greater than critical threshold \((\S+)\)\.$"),
            m => $"{m.Groups[1].Value}: o limite de alerta ({G(m, 2)}) não pode ser maior que o limite crítico ({G(m, 3)})."),
        (Rx(@"^(\S+) must be between (\S+) and (\S+) \(was (\S+)\)\.$"),
            m => $"{m.Groups[1].Value} deve estar entre {G(m, 2)} e {G(m, 3)} (valor: {G(m, 4)})."),
        (Rx(@"^(\S+) \((\S+)\) cannot be greater than (\S+) \((\S+)\)\.$"),
            m => $"{m.Groups[1].Value} ({G(m, 2)}) não pode ser maior que {m.Groups[3].Value} ({G(m, 4)})."),
        (Rx(@"^(\S+) cannot be empty\.$"), m => $"{m.Groups[1].Value} não pode ficar vazio."),
        (Rx(@"^(\S+) contains invalid path characters\.$"), m => $"{m.Groups[1].Value} contém caracteres inválidos para caminho."),
        (Rx(@"^appsettings\.json (?:could not be read|is not valid JSON): (.+)$"), m => $"O arquivo appsettings.json não pôde ser lido: {m.Groups[1].Value}"),
    ];

    /// <summary>Translates a message produced by the shared (English) layers; unknown text is returned unchanged.</summary>
    public static string Message(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (Exact.TryGetValue(text, out var exact))
        {
            return exact;
        }

        foreach (var (pattern, translate) in Templates)
        {
            var match = pattern.Match(text);
            if (match.Success)
            {
                return translate(match);
            }
        }

        return text;
    }

    /// <summary>True when <see cref="Message"/> knows the text (used by tests to detect untranslated templates).</summary>
    public static bool IsKnown(string text) =>
        Exact.ContainsKey(text) || Templates.Any(t => t.Pattern.IsMatch(text) && t.Translate(t.Pattern.Match(text)) != text);

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.CultureInvariant);

    /// <summary>Numeric group with decimal comma.</summary>
    private static string G(Match m, int group) => Num(m.Groups[group].Value);

    private static string CpuWindow(Match m) =>
        m.Groups[1].Value.StartsWith("Average", StringComparison.Ordinal) ? "O uso médio de CPU durante a verificação" : "O uso de CPU";

    private static string List(string englishList) => string.Join(", ", englishList.Split(", ").Select(item =>
    {
        var drives = DrivesCount().Match(item);
        return drives.Success ? $"{drives.Groups[1].Value} unidade(s)" : Component(item);
    }));

    /// <summary>"8 days 14 hours 32 minutes" → "8 dias 14 horas 32 minutos".</summary>
    private static string UptimeText(string english)
    {
        int Part(string unit)
        {
            var m = Regex.Match(english, $@"(\d+) {unit}s?\b", RegexOptions.CultureInvariant);
            return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }

        return Uptime(new TimeSpan(Part("day"), Part("hour"), Part("minute"), 0));
    }

    [GeneratedRegex(@"^(\d+) drive\(s\)$", RegexOptions.CultureInvariant)]
    private static partial Regex DrivesCount();
}
