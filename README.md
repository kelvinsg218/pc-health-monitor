# PC Health Monitor

Ferramenta local de **diagnóstico e monitoramento da saúde** de computadores Windows 10/11, em linha de comando (`pchealth`).

Ela **coleta**, **analisa**, **classifica**, **monitora**, **compara** e **reporta** o estado de CPU, memória, armazenamento, temperatura (quando o Windows expõe uma fonte confiável), processos e uptime, sempre em modo **somente leitura**.

---

## Sumário

- [Problema](#problema)
- [Objetivo](#objetivo)
- [Responsabilidade do produto](#responsabilidade-do-produto)
- [Implementado vs. planejado](#implementado-vs-planejado)
- [Como executar](#como-executar)
- [CLI](#cli)
- [Arquitetura](#arquitetura)
- [Tecnologias](#tecnologias)
- [Métricas coletadas](#métricas-coletadas)
- [HealthAnalyzer](#healthanalyzer)
- [Configuração](#configuração)
- [SQLite e EF Core Migrations](#sqlite-e-ef-core-migrations)
- [Reports](#reports)
- [Logs](#logs)
- [Testes](#testes)
- [Segurança](#segurança)
- [Privacidade](#privacidade)
- [Comportamentos do Windows descobertos na validação](#comportamentos-do-windows-descobertos-na-validação)
- [Limitações](#limitações)
- [Roadmap](#roadmap)

---

## Problema

Quando um usuário diz "o computador está lento", o técnico de TI precisa responder rapidamente:

- A CPU está constantemente sobrecarregada?
- Há pouca memória RAM disponível?
- Algum disco está ficando cheio?
- Qual processo consome mais memória ou CPU?
- Algum componente está em estado crítico?
- Como esta máquina estava em verificações anteriores? O problema está piorando?
- Alguma métrica não pôde ser coletada?

Responder isso normalmente exige abrir várias ferramentas (Gerenciador de Tarefas, Explorer, Monitor de Recursos) e não deixa histórico.

## Objetivo

Oferecer, em um único comando, um diagnóstico **honesto e baseado em evidências**: cada componente é apresentado como **MÉTRICA → ESTADO → EVIDÊNCIA**, com histórico local para comparação ao longo do tempo. A decisão de manutenção fica com o técnico.

## Responsabilidade do produto

O PC Health Monitor **diagnostica**. Ele **não executa manutenção**.

| PC Health Monitor | Windows Maintenance Agent (projeto separado) |
|---|---|
| Coleta, analisa e reporta | Executa manutenção |
| Somente leitura | Altera o sistema |

A ferramenta **não**: instala atualizações, atualiza programas, limpa arquivos, encerra/suspende processos, edita o Registro, desativa serviços, Defender ou Firewall, altera políticas, reinicia a máquina ou muda qualquer configuração. Não há integração com o Windows Maintenance Agent nesta versão (está no roadmap).

---

## Implementado vs. planejado

### IMPLEMENTADO (V1)

- CLI local `pchealth` com os comandos `status`, `scan`, `monitor`, `processes`, `report`, `history` (`list`, `show`, `compare`), `version` e `--help`.
- Coletores Windows: informações do sistema, CPU (amostragem múltipla), memória, armazenamento (discos fixos locais), temperatura (zona térmica ACPI, quando exposta), processos (memória e CPU por intervalo) e uptime.
- `HealthAnalyzer` com estados `OK / WARNING / CRITICAL / UNKNOWN` e saúde geral `HEALTHY / ATTENTION / CRITICAL`.
- Thresholds configuráveis e validados (`appsettings.json`, variáveis de ambiente e opções de linha de comando).
- Histórico em SQLite com EF Core e migrations aplicadas automaticamente na primeira execução.
- Retenção de histórico configurável.
- Comparação histórica simples em pontos percentuais.
- Relatório JSON (obrigatório) + TXT legível em `reports/`.
- Log em arquivo em `logs/`.
- 174 testes automatizados (xUnit).

### PLANEJADO (não implementado — ver [Roadmap](#roadmap))

GUI desktop, gráficos, dashboard, API, monitoramento remoto/centralizado, alertas/notificações, integração com o Windows Maintenance Agent e inventário de múltiplos computadores.

---

## Como executar

### Pré-requisitos

- Windows 10 ou Windows 11.
- .NET SDK 8 ou superior (o projeto tem como alvo **.NET 8 LTS**; com `RollForward=Major` ele também roda em um runtime mais novo, por exemplo quando só o .NET 9 está instalado).
- Não é necessário ser administrador.

### Durante o desenvolvimento

Na raiz do repositório:

```powershell
dotnet restore
dotnet build

dotnet run --project ./src/PCHealthMonitor.Cli -- --help
dotnet run --project ./src/PCHealthMonitor.Cli -- version
dotnet run --project ./src/PCHealthMonitor.Cli -- status
dotnet run --project ./src/PCHealthMonitor.Cli -- scan
dotnet run --project ./src/PCHealthMonitor.Cli -- monitor
dotnet run --project ./src/PCHealthMonitor.Cli -- processes
dotnet run --project ./src/PCHealthMonitor.Cli -- report
dotnet run --project ./src/PCHealthMonitor.Cli -- history

dotnet test
```

> `data/`, `reports/` e `logs/` são criados **no diretório atual** (a raiz do repositório no exemplo acima). Não é preciso criar banco, diretórios nem aplicar migrations manualmente.

### Executável

O projeto CLI gera `pchealth.exe`:

```powershell
dotnet publish ./src/PCHealthMonitor.Cli -c Release -o ./publish
./publish/pchealth.exe status
```

---

## CLI

| Comando | O que faz | Persiste? |
|---|---|---|
| `pchealth status` | Diagnóstico rápido: 1 amostra de CPU (1 s), memória, discos, temperatura | Não |
| `pchealth scan` | Diagnóstico completo: várias amostras de CPU, processos, análise, comparação com o scan anterior | Sim (SQLite) |
| `pchealth monitor` | Visão ao vivo com atualização periódica; Ctrl+C encerra | **Nunca** |
| `pchealth processes` | Top N processos por memória e por CPU | Não |
| `pchealth report` | Scan completo + relatório JSON e TXT | Sim |
| `pchealth history` | Lista scans (`history show <id>`, `history compare [<id> <id>]`) | — |
| `pchealth version` | Versão, runtime e SO | — |
| `pchealth --help` / `pchealth <comando> --help` | Ajuda | — |

### Opções

| Comando | Opções |
|---|---|
| `scan` | `--duration <s>` `--interval <s>` `--top <n>` `--report` `--no-save` |
| `report` | `--duration <s>` `--interval <s>` `--top <n>` `--no-save` |
| `monitor` | `--refresh <s>` `--count <n>` (para após n atualizações) |
| `processes` | `--top <n>` `--sample <s>` |
| `history` | `--limit <n>`, `show <id>`, `compare [<id> <id>]` |
| global | `--no-color`, `-h/--help` |

Números aceitam ponto ou vírgula (`2.5` ou `2,5`).

### Códigos de saída

`0` sucesso · `1` erro de execução (ex.: banco inacessível, scan não encontrado) · `2` uso/configuração inválidos · `130` cancelado com Ctrl+C (o `monitor` encerrado com Ctrl+C retorna `0`, pois é o término normal).

### Exemplo — `status` (saída real da máquina de validação)

```text
================================================================
PC HEALTH MONITOR - STATUS
================================================================
  Machine:      KELVINGC
  Windows:      Windows 11 Home Single Language 25H2 (build 26200.9457, x64)
  Uptime:       12 days 7 hours 15 minutes
  Checked at:   2026-09-27 10:15:08

  COMPONENT     VALUE       STATUS    EVIDENCE
  CPU           24.0%       OK        24.0% (1 sample over 1 s)
  Memory        91.7%       WARNING   5.3 GB of 5.7 GB in use, 487.1 MB available
  Storage C:    48.4%       OK        48.4% used, 244.7 GB free of 474.7 GB
  Temperature   39.1 °C     OK        39.1 °C (ACPI thermal zone TZ01, not necessarily the CPU core)

----------------------------------------------------------------
Overall Health: ATTENTION

Reason:
  [WARNING] Memory usage is 91.7% (5.3 GB of 5.7 GB in use, 487.1 MB available), above the warning threshold of 80%.
```

### Exemplo — `history`

```text
ID    DATE               HOST              CPU AVG  CPU PEAK   MEMORY   DISK MAX      HEALTH
------------------------------------------------------------------------------------------------
#3    2026-09-27 10:24   KELVINGC             8.3%      9.7%    89.9%   C: 48.4%      ATTENTION
#2    2026-09-27 10:24   KELVINGC             9.7%     14.1%    89.3%   C: 48.4%      ATTENTION
#1    2026-09-27 10:15   KELVINGC            10.1%     16.1%    93.0%   C: 48.4%      CRITICAL
```

A saída da CLI é em inglês (rótulos técnicos padronizados e fáceis de colar em chamados); esta documentação é em português.

---

## Arquitetura

```
PCHealthMonitor.sln
src/
  PCHealthMonitor.Domain/          modelos, enums, estados, ThresholdPair (regra de classificação),
                                   estatísticas de amostras, resolução da versão do Windows
  PCHealthMonitor.Application/     abstrações dos coletores, HealthAnalyzer, CpuSampler,
                                   cálculo de CPU por processo, HealthCheckService (status/scan),
                                   ScanWorkflow, MonitorService, histórico/comparação,
                                   modelo do relatório, configuração e validação
  PCHealthMonitor.Infrastructure/  coletores Windows, SQLite/EF Core + migrations,
                                   escrita de relatórios JSON/TXT, logger em arquivo
  PCHealthMonitor.Cli/             parser de argumentos, composição, comandos, renderização
tests/
  PCHealthMonitor.Tests/           testes xUnit (fakes para todos os coletores)
```

Dependências: `Cli → Infrastructure → Application → Domain`.

Princípios:

- **Coleta separada de análise.** Coletores (`ICpuCollector`, `IMemoryCollector`, `IStorageCollector`, `ITemperatureProvider`, `IProcessCollector`, `ISystemInfoCollector`) só coletam dados brutos; quem decide OK/WARNING/CRITICAL é o `HealthAnalyzer`. Assim as regras são testadas sem depender da máquina.
- **Resiliência.** Cada coletor é isolado: uma falha vira `UNKNOWN` + mensagem amigável e o scan continua.
- **CLI fina.** Nenhuma regra de diagnóstico mora na CLI; ela apenas lê argumentos e formata a saída.
- **Proporcionalidade.** Composição manual (sem contêiner de DI), parser de argumentos próprio e poucas dependências.

## Tecnologias

| Item | Uso |
|---|---|
| C# / .NET 8 (LTS) | Alvo `net8.0` (Domain/Application) e `net8.0-windows` (Infrastructure/CLI/Testes) |
| Microsoft.EntityFrameworkCore.Sqlite 8.0.31 | Histórico em SQLite |
| Microsoft.EntityFrameworkCore.Design 8.0.31 | Geração de migrations (somente design-time) |
| System.Management 8.0.0 | Leitura WMI da zona térmica (sem `WMIC.exe`) |
| Microsoft.Extensions.Configuration.* 8.0.x | `appsettings.json` + variáveis de ambiente |
| Microsoft.Extensions.Logging 8.0.x | Logging (provedor de arquivo próprio, mínimo) |
| xUnit 2.9.3 | Testes |
| dotnet-ef 8.0.31 | Ferramenta local (`.config/dotnet-tools.json`) |

APIs Windows usadas (todas de leitura): `GetSystemTimes`, `GlobalMemoryStatusEx`, `GetPhysicallyInstalledSystemMemory`, `NtQuerySystemInformation(SystemProcessInformation)`, `DriveInfo`, leitura do Registro (`HKLM\...\CurrentVersion`, `HKLM\HARDWARE\...\CentralProcessor\0`) e a classe WMI `Win32_PerfRawData_Counters_ThermalZoneInformation`. **`WMIC.exe` não é usado.**

---

## Métricas coletadas

### Informações do sistema

Hostname, nome do Windows (10/11), edição, versão (ex.: `25H2`), build (`26200.9457`), arquitetura, uptime, usuário atual e timestamp.
O nome **Windows 10/11 é decidido pelo número do build** (≥ 22000 → Windows 11), porque o valor `ProductName` do Registro continua dizendo "Windows 10" no Windows 11 (confirmado na máquina de validação). Valores ausentes aparecem como `Unavailable` — nada é inventado.

### CPU

- Modelo (Registro), número de processadores lógicos.
- Uso total calculado por diferença de `GetSystemTimes` (tempo ocupado ÷ tempo total no intervalo).
- **Scan:** várias amostras (padrão: 5 × 1 s) → **mínimo, máximo (pico) e média**. O estado usa a **média**; o pico é apresentado como evidência (e gera nota informativa se ultrapassar o limite crítico).
- **Status:** uma amostra de 1 s. **Monitor:** uma amostra por atualização (a própria medição é o intervalo, sem loop ocioso).
- A estratégia é testável: o `CpuSampler` usa `ICpuCollector`, substituído por fake nos testes.

### Memória

`GlobalMemoryStatusEx`: total utilizável pelo Windows, disponível, usado (= total − disponível) e percentual. Também é mostrada a RAM instalada (`GetPhysicallyInstalledSystemMemory`) quando difere — na máquina de validação: 8,0 GB instalados, 5,7 GB utilizáveis (parte reservada para a GPU integrada). Valores internos em bytes; exibição em unidades binárias (1 GB = 1024³ bytes, como o Windows).

### Armazenamento

Somente **discos fixos locais com letra** (`DriveType.Fixed`): nome, sistema de arquivos, rótulo, total, usado, livre (`TotalFreeSpace`, o mesmo do Explorer) e percentual.
Unidades ópticas vazias, removíveis e de rede são ignoradas para não serem tratadas como problema de saúde. Um disco fixo não pronto (ex.: BitLocker bloqueado) aparece como `Unavailable/UNKNOWN` sem impedir a análise dos demais.

### Temperatura

- Fonte: **zonas térmicas ACPI** expostas pelo Windows no contador de performance *Thermal Zone Information* (via WMI `Win32_PerfRawData_Counters_ThermalZoneInformation`). Funciona sem administrador (ao contrário de `MSAcpi_ThermalZoneTemperature`, que retornou *Acesso negado*).
- **Uma zona térmica ACPI é definida pelo firmware e não é necessariamente o sensor do núcleo da CPU.** Isso é informado na saída e no relatório.
- Valores implausíveis (≤ 1 °C, ≥ 125 °C, placeholders como 0 K ou exatamente 0 °C) são descartados.
- Se não houver zona plausível → `Temperature: Unavailable` / `Status: UNKNOWN`, **sem crash** e sem inventar valor.
- Se o Windows informar *passive throttling* (limite passivo < 100%), o estado é ao menos `WARNING`.
- Nenhuma dependência pesada ou driver de terceiros (ex.: bibliotecas que carregam drivers de kernel) foi introduzida só para exibir temperatura.

### Processos

- Fonte principal: lista de processos do sistema (`NtQuerySystemInformation`, a mesma usada pelo Gerenciador de Tarefas e pelo próprio `Process.GetProcesses()` do .NET). Ela traz **tempo de CPU e working set de todos os processos sem abrir handles**, portanto **sem exigir administrador**.
- Fallback: `System.Diagnostics.Process`; processos protegidos que negam acesso ficam com CPU `Unavailable` e a coleta continua.
- **CPU por processo** = Δ tempo de CPU do processo ÷ (Δ tempo decorrido × processadores lógicos) × 100, limitado a 0–100%, medido entre dois snapshots (no `scan`, a janela é a mesma da amostragem de CPU; no `processes`, 1 s por padrão).
- Processos que terminaram durante a medição são descartados; processos que começaram durante a medição ou PIDs reutilizados ficam com CPU `Unavailable`. O processo Idle (PID 0) é excluído.
- Memória exibida = **working set** (inclui páginas compartilhadas). O Gerenciador de Tarefas mostra, por padrão, o *working set privado*, então os números dele tendem a ser menores.
- Coleta apenas PID, nome, working set e tempo de CPU (sem linha de comando, títulos de janela etc.).

### Uptime

`GetTickCount64` (tempo desde o boot do kernel), exibido como `8 days 14 hours 32 minutes`. É **informativo**: a partir de `UptimeNoticeDays` (padrão 30) aparece uma nota `INFO`, que nunca altera a saúde. Com a *Inicialização Rápida* do Windows, "Desligar" não reinicia o kernel, então o uptime só zera em uma reinicialização real.

---

## HealthAnalyzer

### Estados por componente

| Estado | Significado |
|---|---|
| `OK` | Abaixo do limite de aviso |
| `WARNING` | ≥ limite de aviso |
| `CRITICAL` | ≥ limite crítico |
| `UNKNOWN` | Métrica indisponível, não suportada ou falha não crítica de coleta |

Os limites são **inclusivos**: com CPU warning = 75 e critical = 90, `74.9 → OK`, `75 → WARNING`, `89.9 → WARNING`, `90 → CRITICAL`.

Regras:

- **CPU:** média da janela de amostragem.
- **Memória:** percentual usado da memória utilizável.
- **Armazenamento:** cada disco é classificado; o componente assume o pior disco.
- **Temperatura:** maior zona térmica plausível; *throttling* reportado ⇒ pelo menos `WARNING`; indisponível ⇒ `UNKNOWN`.

### Overall Health

1. Se algum componente estiver `CRITICAL` → **CRITICAL**.
2. Senão, se algum estiver `WARNING` → **ATTENTION**.
3. Senão → **HEALTHY**.

`UNKNOWN` **nunca** eleva sozinho o estado geral. Se CPU, memória ou armazenamento estiverem `UNKNOWN` (ou 2+ componentes quaisquer, ou algum disco ilegível), o diagnóstico é marcado como **dados incompletos** e isso é informado. Se **nenhum** componente puder ser avaliado, o resultado é **ATTENTION** com a mensagem "diagnóstico inconclusivo" (nunca `HEALTHY` sem dados, nunca `CRITICAL` por falta de dados).

### Mensagens

As mensagens descrevem somente o que foi medido, com o valor e o limite:

- `Memory usage is 91.7% (5.3 GB of 5.7 GB in use, 487.1 MB available), above the warning threshold of 80%.`
- `Drive C: has less free space than recommended: 88.4% used (55.0 GB free), above the warning threshold of 80%.`
- `CPU reached a peak of 94.0% during the scan (average 37.0%).`
- `Temperature could not be obtained on this computer.`

A ferramenta **não** afirma "CPU defeituosa", "precisa trocar memória", "disco falhando" ou "superaquecimento" — há teste automatizado garantindo que essas palavras não aparecem nas mensagens.

## Thresholds

| Métrica | WARNING | CRITICAL |
|---|---|---|
| CPU (média) | ≥ 75% | ≥ 90% |
| Memória | ≥ 80% | ≥ 92% |
| Armazenamento (por disco) | ≥ 80% | ≥ 90% |
| Temperatura (zona ACPI) | ≥ 85 °C | ≥ 95 °C |

São **defaults de produto**, não verdades universais; todos são configuráveis.

---

## Configuração

Arquivo `src/PCHealthMonitor.Cli/appsettings.json` (copiado para a saída do build). A aplicação funciona **sem nenhuma configuração** (os mesmos defaults estão no código).

```json
{
  "PcHealth": {
    "Thresholds": {
      "CpuWarning": 75, "CpuCritical": 90,
      "MemoryWarning": 80, "MemoryCritical": 92,
      "StorageWarning": 80, "StorageCritical": 90,
      "TemperatureWarning": 85, "TemperatureCritical": 95,
      "UptimeNoticeDays": 30
    },
    "Scan": { "DurationSeconds": 5, "SampleIntervalSeconds": 1, "StatusCpuSampleSeconds": 1 },
    "Monitor": { "RefreshIntervalSeconds": 2 },
    "Processes": { "TopCount": 10, "CpuSampleSeconds": 1 },
    "History": { "RetentionDays": 365, "DefaultListLimit": 20 },
    "Paths": { "DataDirectory": "data", "ReportsDirectory": "reports", "LogsDirectory": "logs", "DatabaseFileName": "pchealth.db" }
  }
}
```

Precedência: `appsettings.json` → variáveis de ambiente `PCHEALTH_` (ex.: `PCHEALTH_Scan__DurationSeconds=10`) → opções de linha de comando (`--duration 10`).

Validação (erro claro, código de saída 2):

- percentuais em (0, 100]; temperatura em (0, 150];
- warning **não pode ser maior** que critical;
- `Scan.DurationSeconds` 1–600; `Scan.SampleIntervalSeconds` 0,25–60 e ≤ duração; `Monitor.RefreshIntervalSeconds` 0,5–3600; `Processes.TopCount` 1–100; `History.RetentionDays` 0–36500 (0 = manter tudo);
- chaves desconhecidas (erros de digitação) e valores não numéricos são rejeitados.

```text
Invalid configuration:
  - Thresholds.Cpu: warning threshold (95) cannot be greater than critical threshold (90).
```

---

## SQLite e EF Core Migrations

- Banco: `data/pchealth.db` (relativo ao diretório atual; configurável).
- Na primeira execução de `scan`, `report` ou `history`, o diretório é criado e as migrations pendentes são aplicadas (`Database.Migrate()`), com log. `status`, `monitor` e `processes` não abrem o banco.
- **Somente scans completos são persistidos.** O `monitor` não tem acesso ao repositório (garantido estruturalmente e por teste).
- Retenção: após salvar um scan, scans mais antigos que `History.RetentionDays` são removidos (cascade para discos e mensagens).

Modelo:

| Tabela | Conteúdo |
|---|---|
| `Scans` | Id, Hostname, StartedAtUtc, FinishedAtUtc, WindowsVersion, CpuAverage, CpuPeak, CpuMinimum, CpuSampleCount, MemoryUsagePercent, MemoryTotalBytes, MemoryUsedBytes, TemperatureCelsius (nulo se indisponível), UptimeSeconds, CpuStatus, MemoryStatus, StorageStatus, TemperatureStatus, OverallHealth, IsIncomplete |
| `ScanDrives` | Id, ScanId (FK), Name, FileSystem, TotalBytes, FreeBytes, UsagePercent, Status |
| `ScanMessages` | Id, ScanId (FK), Position, Severity, Component, Text |

Lista de processos não é persistida (fica apenas no relatório), evitando transformar o banco em cópia de cada detalhe do sistema.

Criar uma nova migration:

```powershell
dotnet tool restore
cd src/PCHealthMonitor.Infrastructure
dotnet ef migrations add <Nome> --output-dir Persistence/Migrations
```

> Execute a partir do diretório do projeto: o `dotnet-ef` 8 falha ao invocar o MSBuild quando o caminho do repositório contém espaços e o projeto é passado com `--project`.

## Histórico e comparação

- `history` lista os scans (mais recentes primeiro); `*` indica dados incompletos.
- `history show <id>` mostra detalhes, mensagens e a comparação com o scan anterior da mesma máquina.
- `history compare` compara o último scan com o anterior; `history compare <id> <id>` compara dois scans.
- `scan` e `report` mostram automaticamente as mudanças desde o scan anterior.

A comparação é aritmética simples, apresentada como **mudança observada (não previsão)** e em **pontos percentuais**: de 70% para 80% é `+10.0 percentage points` (não "10%").

```text
CHANGES SINCE PREVIOUS SCAN #1 (2026-09-27 10:15)
  Observed change between the two scans (not a prediction).
  METRIC                  PREVIOUS   CURRENT   CHANGE
  CPU average                10.1%      9.7%   -0.5 percentage points
  Memory usage               93.0%     89.3%   -3.7 percentage points
  Drive C: usage             48.4%     48.4%   no change (0.0 percentage points)
  Overall health          CRITICAL ATTENTION   changed
```

## Reports

`pchealth report` (ou `pchealth scan --report`) grava em `reports/`:

- `pchealth_HOSTNAME_YYYY-MM-DD_HHMMSS.json` (obrigatório; nunca sobrescreve — adiciona sufixo `_2` se necessário);
- `pchealth_HOSTNAME_YYYY-MM-DD_HHMMSS.txt` (versão legível).

Campos do JSON (camelCase): `schemaVersion`, `tool`, `toolVersion`, `timestamp`, `hostname`, `scanId`, `startedAt`, `finishedAt`, `system`, `uptime`, `cpu`, `memory`, `storage`, `temperature`, `processSummary`, `componentStatuses`, `overallHealth`, `incompleteData`, `diagnosticMessages`, `thresholds`.
Dados ausentes são `null` (ex.: `temperature.celsius: null`, `temperature.status: "UNKNOWN"`, com `unavailableReason`).

## Logs

`logs/pchealth-YYYYMMDD.log`: início de comando, conclusão de scan, migrations, falhas de coletores, banco e relatório, e exceções inesperadas (com detalhes técnicos). O console mostra apenas mensagens amigáveis. O monitor registra cada falha de métrica uma única vez por sessão, evitando logs excessivos. Não são registrados dados pessoais além do nome da máquina e dos caminhos.

---

## Testes

```powershell
dotnet test
dotnet test --filter "Category!=WindowsIntegration"   # somente testes independentes da máquina
```

**174 testes** (xUnit), todos passando na validação:

| Área | Cobertura |
|---|---|
| HealthAnalyzer | CPU/memória/armazenamento OK/WARNING/CRITICAL, limites exatos (74.9/75/89.9/90, 79.9/80/91.9/92…), temperatura UNKNOWN e classificada, throttling, overall HEALTHY/ATTENTION/CRITICAL, UNKNOWN não gera CRITICAL, dados incompletos, inconclusivo, mensagens sem causalidade inventada |
| Configuração | defaults, customização, warning > critical, negativo, > 100, NaN, intervalo/duração/refresh/top/retention inválidos, `appsettings.json`, chave desconhecida, JSON malformado, overrides |
| Amostragem | mín/máx/média/pico (20, 40, 60, 80 → média 50, pico 80), amostras inválidas, contagem de amostras, cancelamento, cálculo por `GetSystemTimes` — sem timing real |
| Processos | ordenação por memória e CPU, Top N, processo inacessível, processo encerrado durante a coleta, processo novo, PID reutilizado, valores inválidos, PID 0 |
| Histórico | persistência, leitura, ordenação, múltiplos scans, scan anterior por máquina, retenção, comparação em pontos percentuais |
| SQLite | migrations aplicadas em banco em memória, tabelas, relacionamentos, cascade, criação do arquivo/diretório, erro amigável |
| Reports | JSON válido, campos obrigatórios, valores, temperatura `null`/UNKNOWN, nome do arquivo, sem sobrescrita, estrutura do TXT |
| Monitor | N atualizações, cancelamento (Ctrl+C simulado) sem exceção, falhas viram `Unavailable`, monitor não grava no banco |
| Scan/Workflow | resiliência a exceções dos coletores, falha do banco não perde o resultado, `--no-save` |
| Descobertas do Windows | ProductName "Windows 10" com build 26200 ⇒ Windows 11; zona térmica 3092 (décimos de Kelvin) ⇒ ~36 °C; placeholders descartados |
| CLI | parser, opções inválidas, renderização de status/scan/comparação, formatação |
| Integração Windows (`Category=WindowsIntegration`, 6 testes) | coletores reais, somente leitura, verificando apenas invariantes (0–100%, disponível ≤ total etc.) |

Os testes não usam o banco real do usuário (SQLite em memória ou diretórios temporários) e, exceto a categoria `WindowsIntegration`, não dependem de CPU, RAM, discos, temperatura ou processos reais.

---

## Segurança

- Somente leitura: nenhuma chamada altera o sistema. Leituras de Registro são apenas de consulta.
- Não mata, suspende nem altera prioridade de processos.
- Não desativa Defender/Firewall, não adiciona exclusões, não altera serviços, políticas ou atualizações.
- Não deleta arquivos (os únicos arquivos escritos são `data/`, `reports/` e `logs/` da própria ferramenta).
- Não solicita elevação nem tenta contornar UAC; o que exigir privilégio aparece como `Unavailable`.

## Privacidade

Coleta apenas o necessário para diagnóstico técnico: métricas de hardware/SO, hostname, nome do usuário atual e, dos processos, somente PID, nome, working set e tempo de CPU.
**Não** coleta conteúdo de arquivos, histórico de navegador, senhas, clipboard, teclas, screenshots, documentos, mensagens, linhas de comando ou títulos de janelas. Tudo fica local (sem telemetria).

---

## Comportamentos do Windows descobertos na validação

Validação feita em Windows 11 Home Single Language 25H2 (build 26200.9457), AMD Ryzen 5 7520U (8 lógicos), usuário **sem** privilégios de administrador:

1. **`ProductName` inconsistente:** o Registro informa `Windows 10 Home Single Language` num Windows 11 (build 26200). A ferramenta usa o build; há teste reproduzindo o caso.
2. **CPU por processo via `Process.TotalProcessorTime`:** falhou (acesso negado) para 142 de 307 processos sem admin — incluindo `MsMpEng` (Defender) e `svchost`, que costumam aparecer em investigações de lentidão. O ranking de CPU ficaria enganoso. **Corrigido** usando a lista de processos do sistema (`NtQuerySystemInformation`): 306 de 306 processos com CPU medida.
3. **Temperatura:** `MSAcpi_ThermalZoneTemperature` (root\wmi) → *Acesso negado* sem admin. O contador *Thermal Zone Information* expõe `\_TZ.TZ01` com `HighPrecisionTemperature` em **décimos de Kelvin** (3092 → 36,1 °C) e variou entre leituras (35–39 °C), por isso é usado — rotulado como zona ACPI, não sensor de núcleo.
4. **Nomes de contadores localizados:** em pt-BR os contadores de performance têm nomes traduzidos (`\Processador(_Total)\% Tempo de Processador`). A ferramenta não depende desses nomes (usa APIs e classes WMI não localizadas).
5. **CPU total vs. Gerenciador de Tarefas:** o valor calculado acompanha `% Tempo de Processador` (diferenças de poucos pontos por defasagem das janelas). O Gerenciador de Tarefas no Windows 10 1903+/11 mostra `% Utilidade do Processador`, que considera a frequência (turbo/economia), então pode diferir.
6. **Memória utilizável < instalada:** 8,0 GB instalados, 5,7 GB utilizáveis (reserva de hardware/iGPU). A ferramenta mostra as duas e calcula o uso sobre a utilizável, como o Windows.
7. **Working set** idêntico ao `Get-Process` (verificado nos 5 maiores processos). **Discos** idênticos ao `Get-Volume`. **Uptime** consistente com `Win32_OperatingSystem.LastBootUpTime`.
8. **Partição sem letra** (recuperação, 2 GB) não aparece no `DriveInfo` e não é analisada — comportamento desejado.

## Limitações

- Somente Windows 10/11; os coletores são específicos do Windows.
- A temperatura depende do firmware: muitas máquinas não expõem zona térmica, e a zona ACPI não é o sensor do núcleo da CPU. Sensores de núcleo/SMART exigiriam drivers de terceiros ou privilégios, e não foram incluídos.
- O uso de CPU é baseado em tempo (não em frequência); pode diferir da "Utilidade" do Gerenciador de Tarefas.
- `status` usa uma única amostra de CPU (retrato instantâneo); para diagnóstico use `scan`.
- A memória por processo é o working set total (não o privado).
- Saúde física do disco (SMART), GPU, rede, bateria e eventos do Windows não são avaliados na V1.
- `data/`, `reports/` e `logs/` são relativos ao diretório atual (configurável em `Paths`).
- A leitura da lista de processos usa `NtQuerySystemInformation`, uma API do Windows documentada como sujeita a mudanças; existe fallback automático para `System.Diagnostics.Process`.
- Ctrl+C foi validado com um evento real `CTRL_C_EVENT`; em shells que criam processos com Ctrl+C desabilitado (ex.: alguns executores automatizados), o sinal não chega ao processo — nesse caso use `monitor --count <n>`.

## Roadmap

Possibilidades futuras (**não implementadas**):

- GUI desktop
- Gráficos de histórico
- Dashboard
- API
- Monitoramento remoto
- Monitoramento centralizado de múltiplas máquinas
- Alertas e notificações
- Integração com o **Windows Maintenance Agent** (ex.: sugerir manutenção a partir do diagnóstico, mantendo a execução naquele projeto)
- Inventário de múltiplos computadores
- Saúde física de discos (SMART), bateria e eventos do Windows
