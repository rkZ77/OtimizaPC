using System.Globalization;
using Fpsx.Core.Model;
using Fpsx.Core.Optimizations;

namespace Fpsx.Core.Diagnostics;

internal static class Fmt
{
    public static string Gb(long bytes) => (bytes / 1024.0 / 1024 / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " GB";

    public static string Pct(double v) => v.ToString("0", CultureInfo.InvariantCulture) + "%";

    public static string Num(double v, string format = "0.0") => v.ToString(format, CultureInfo.InvariantCulture);
}

public sealed class WindowsVersionDiagnostic : IDiagnostic
{
    public string Id => "windows-version";

    public IEnumerable<Finding> Run(EvaluationContext context)
    {
        var os = context.Snapshot.Os;
        var evidence = new Dictionary<string, string> { ["sistema"] = os.Caption, ["build"] = os.Build.ToString(CultureInfo.InvariantCulture) };
        if (os.Build == 0)
        {
            yield return new Finding { DiagnosticId = Id, Area = Areas.Windows, Status = HealthStatus.Unknown, Title = "Versão do Windows não identificada", Evidence = evidence };
            yield break;
        }

        yield return os.IsWindows11
            ? new Finding { DiagnosticId = Id, Area = Areas.Windows, Status = HealthStatus.Ok, Title = $"{os.Caption} (build {os.Build})", Detail = "Versão suportada.", Evidence = evidence }
            : new Finding
            {
                DiagnosticId = Id, Area = Areas.Windows, Status = HealthStatus.Info,
                Title = $"{os.Caption} (build {os.Build})",
                Detail = "Esta versão do RKZFPS foi validada no Windows 11. No Windows 10 o diagnóstico funciona, mas algumas otimizações podem não se aplicar.",
                Evidence = evidence,
            };
    }
}

/// <summary>
/// Throttling é problema físico (refrigeração, fonte, BIOS). O diagnóstico
/// diz isso com todas as letras e não oferece tweak nenhum como solução.
/// </summary>
public sealed class CpuThrottlingDiagnostic : IDiagnostic
{
    public string Id => "cpu-throttling";

    public IEnumerable<Finding> Run(EvaluationContext context)
    {
        var cpu = context.Snapshot.Cpu;
        if (cpu is null)
        {
            yield return new Finding { DiagnosticId = Id, Area = Areas.Cpu, Status = HealthStatus.Unknown, Title = "CPU não identificada" };
            yield break;
        }

        var evidence = new Dictionary<string, string>
        {
            ["cpu"] = cpu.Name,
            ["nucleos_threads"] = $"{cpu.Cores}/{cpu.Threads}",
            ["limite_medio"] = cpu.AvgPerformanceLimitPercent is { } a ? Fmt.Pct(a) : "desconhecido",
            ["limite_minimo"] = cpu.MinPerformanceLimitPercent is { } m ? Fmt.Pct(m) : "desconhecido",
            ["temperatura"] = cpu.TemperatureC is { } t ? Fmt.Num(t, "0") + " °C" : "não disponível",
            ["amostra"] = cpu.SampledUnderLoad ? "sob carga (teste de CPU)" : "em repouso",
        };

        var when = cpu.SampledUnderLoad ? "durante o teste de carga" : "no momento do scan";
        if (cpu.TemperatureC is >= 95)
        {
            yield return new Finding
            {
                DiagnosticId = Id, Area = Areas.Cpu, Status = HealthStatus.Problem, ImpactArea = "FPS",
                Title = "Temperatura da CPU no limite",
                Detail = $"A CPU chegou a {Fmt.Num(cpu.TemperatureC.Value, "0")} °C {when}. Nessa faixa o processador reduz o clock para se proteger.",
                Recommendation = "Verifique a refrigeração (pasta térmica, cooler, poeira, fluxo de ar do gabinete) antes de qualquer ajuste de software.",
                Evidence = evidence,
            };
            yield break;
        }

        if (cpu.MinPerformanceLimitPercent is { } min && min < 90)
        {
            yield return new Finding
            {
                DiagnosticId = Id, Area = Areas.Cpu, Status = cpu.SampledUnderLoad ? HealthStatus.Problem : HealthStatus.Attention, ImpactArea = "FPS",
                Title = "CPU com limitação de desempenho",
                Detail = $"O contador de limite de desempenho do processador caiu para {Fmt.Pct(min)} {when}. A CPU foi impedida de usar toda a capacidade por temperatura, limite de energia ou configuração de energia.",
                Recommendation = cpu.SampledUnderLoad
                    ? "Verifique a refrigeração e se o notebook está na tomada. Nenhum tweak de software resolve limitação térmica."
                    : "Rode o teste de CPU (fpsx scan --cpu-test) para confirmar sob carga. Em repouso, a limitação pode ser só economia de energia.",
                Evidence = evidence,
            };
            yield break;
        }

        if (cpu.AvgPerformanceLimitPercent is null)
        {
            yield return new Finding { DiagnosticId = Id, Area = Areas.Cpu, Status = HealthStatus.Unknown, Title = cpu.Name, Detail = "Não foi possível ler o contador de limitação da CPU.", Evidence = evidence };
            yield break;
        }

        yield return new Finding
        {
            DiagnosticId = Id, Area = Areas.Cpu, Status = HealthStatus.Ok, Title = cpu.Name,
            Detail = $"Sem limitação de desempenho detectada {when}.", Evidence = evidence,
        };
    }
}

public sealed class BackgroundLoadDiagnostic : IDiagnostic
{
    public string Id => "cpu-background-load";

    public IEnumerable<Finding> Run(EvaluationContext context)
    {
        var s = context.Snapshot;
        if (s.Processes.Count == 0 || s.Cpu?.AvgUsagePercent is not { } usage)
        {
            yield return new Finding { DiagnosticId = Id, Area = Areas.Cpu, Status = HealthStatus.Unknown, Title = "Uso de CPU em segundo plano não medido" };
            yield break;
        }

        var gameProcesses = context.GameProfiles.SelectMany(p => p.Detect.Processes).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var heavy = s.Processes
            .Where(p => p.CpuPercent >= 5 && !gameProcesses.Contains(p.Name) && ProcessClassifier.Classify(p.Name) != ProcessCategory.Critical)
            .OrderByDescending(p => p.CpuPercent)
            .Take(5)
            .ToList();
        var evidence = heavy.ToDictionary(p => $"{p.Name} ({p.Pid})", p => $"{Fmt.Pct(p.CpuPercent)} CPU, {Fmt.Gb(p.WorkingSetBytes)}");
        evidence["uso_total"] = Fmt.Pct(usage);

        if (heavy.Count == 0 && usage < 25)
        {
            yield return new Finding
            {
                DiagnosticId = Id, Area = Areas.Cpu, Status = HealthStatus.Ok, Title = "Pouca carga em segundo plano",
                Detail = $"Uso total de CPU de {Fmt.Pct(usage)} em {Fmt.Num(s.SampleSeconds, "0")} s de amostra.", Evidence = evidence,
            };
            yield break;
        }

        yield return new Finding
        {
            DiagnosticId = Id, Area = Areas.Cpu, Status = HealthStatus.Attention, ImpactArea = "STUTTER",
            Title = "Processos consumindo CPU em segundo plano",
            FixOptimizationId = "background-process-close",
            Detail = heavy.Count > 0
                ? $"{string.Join(", ", heavy.Select(p => $"{p.Name} ({Fmt.Pct(p.CpuPercent)})"))} consumiram CPU durante a amostra. Carga de fundo disputa núcleos com o jogo e aparece como queda no 1% low."
                : $"Uso total de CPU em repouso de {Fmt.Pct(usage)}, alto para um PC sem jogo aberto.",
            Recommendation = "Feche esses programas antes de jogar. O RKZFPS não encerra processos sozinho.",
            Evidence = evidence,
        };
    }
}

public sealed class MemoryDiagnostic : IDiagnostic
{
    public string Id => "memory-pressure";

    public IEnumerable<Finding> Run(EvaluationContext context)
    {
        var s = context.Snapshot;
        var m = s.Memory;
        if (m is null || m.TotalBytes <= 0)
        {
            yield return new Finding { DiagnosticId = Id, Area = Areas.Ram, Status = HealthStatus.Unknown, Title = "Memória não lida" };
            yield break;
        }

        var top = s.Processes.OrderByDescending(p => p.WorkingSetBytes).Take(5).ToList();
        var evidence = new Dictionary<string, string>
        {
            ["total"] = Fmt.Gb(m.TotalBytes),
            ["disponivel"] = Fmt.Gb(m.AvailableBytes),
            ["uso"] = Fmt.Pct(m.UsedPercent),
        };
        foreach (var p in top)
            evidence[$"{p.Name} ({p.Pid})"] = Fmt.Gb(p.WorkingSetBytes);

        var totalGb = m.TotalBytes / 1024.0 / 1024 / 1024;
        // O jogo aberto não entra na sugestão de "feche antes de jogar".
        var topNames = string.Join(", ", top.Where(p => ProcessClassifier.Classify(p.Name) is ProcessCategory.User or ProcessCategory.Launcher).Take(3).Select(p => p.Name));

        if (m.UsedPercent >= 90)
        {
            yield return new Finding
            {
                DiagnosticId = Id, Area = Areas.Ram, Status = HealthStatus.Problem, ImpactArea = "STUTTER",
                Title = "Pressão de memória",
                FixOptimizationId = "background-process-close",
                Detail = $"{Fmt.Pct(m.UsedPercent)} da RAM em uso no momento do scan, antes de abrir o jogo. Isso causa paginação em disco, stutter e carregamento lento.",
                Recommendation = $"Feche aplicações antes de jogar. Maiores consumidores agora: {topNames}.",
                Evidence = evidence,
            };
        }
        else if (m.UsedPercent >= 80)
        {
            yield return new Finding
            {
                DiagnosticId = Id, Area = Areas.Ram, Status = HealthStatus.Attention, ImpactArea = "STUTTER",
                Title = "Uso de memória alto",
                FixOptimizationId = "background-process-close",
                Detail = $"{Fmt.Pct(m.UsedPercent)} da RAM em uso sem o jogo aberto. Sobra pouco para o jogo.",
                Recommendation = $"Feche aplicações antes de jogar. Maiores consumidores agora: {topNames}.",
                Evidence = evidence,
            };
        }
        else
        {
            yield return new Finding
            {
                DiagnosticId = Id, Area = Areas.Ram, Status = HealthStatus.Ok, Title = $"RAM: {Fmt.Gb(m.TotalBytes)}",
                Detail = $"{Fmt.Pct(m.UsedPercent)} em uso no momento do scan.", Evidence = evidence,
            };
        }

        // 7.5 e não 8: um pente de 8 GB aparece como ~7.8 GB com vídeo integrado reservando parte.
        if (totalGb < 7.5)
        {
            yield return new Finding
            {
                DiagnosticId = Id, Area = Areas.Ram, Status = HealthStatus.Attention, ImpactArea = "STUTTER",
                Title = "Menos de 8 GB de RAM",
                Detail = "Jogos atuais, incluindo o CS2, pedem no mínimo 8 GB. Abaixo disso o stutter por paginação é esperado e não se resolve com software.",
                Recommendation = "Upgrade de memória é a mudança com maior efeito neste PC.",
                Evidence = evidence,
            };
        }

        if (m.PagefilePresent == false && totalGb <= 16.5)
        {
            yield return new Finding
            {
                DiagnosticId = Id, Area = Areas.Ram, Status = HealthStatus.Attention, ImpactArea = "SYSTEM",
                Title = "Arquivo de paginação desativado",
                FixOptimizationId = "pagefile-restore-automatic",
                Detail = "Sem arquivo de paginação, quando a memória acaba o jogo fecha em vez de ficar lento. Desativar paginação é um tweak popular que não aumenta FPS.",
                Recommendation = "Reative em Sistema > Sobre > Configurações avançadas > Desempenho > Memória virtual: \"Gerenciar automaticamente\".",
                Evidence = evidence,
            };
        }
    }
}

public sealed class StorageDiagnostic : IDiagnostic
{
    public string Id => "storage-health";

    public IEnumerable<Finding> Run(EvaluationContext context)
    {
        var s = context.Snapshot;
        if (s.Disks.Count == 0)
        {
            yield return new Finding { DiagnosticId = Id, Area = Areas.Storage, Status = HealthStatus.Unknown, Title = "Discos não lidos" };
            yield break;
        }

        var gameDrives = s.Games
            .Where(g => g.InstallPath.Length >= 2)
            .ToLookup(g => g.InstallPath[..2].ToUpperInvariant(), g => g.Name);
        var anyIssue = false;

        foreach (var d in s.Disks)
        {
            var evidence = new Dictionary<string, string>
            {
                ["livre"] = $"{Fmt.Gb(d.FreeBytes)} de {Fmt.Gb(d.TotalBytes)} ({Fmt.Pct(d.FreePercent)})",
                ["tipo"] = d.Media switch { MediaKind.Ssd => "SSD", MediaKind.Hdd => "HDD", _ => "desconhecido" },
                ["barramento"] = d.BusType,
                ["saude"] = d.Health,
                ["estado"] = d.OperationalStatus,
                ["modelo"] = d.Model,
            };
            var games = gameDrives[d.DriveLetter.ToUpperInvariant()].ToList();

            if (d.Health is "Warning" or "Unhealthy")
            {
                anyIssue = true;
                yield return new Finding
                {
                    DiagnosticId = Id, Area = Areas.Storage, Status = HealthStatus.Problem, ImpactArea = "SYSTEM",
                    Title = $"Disco {d.DriveLetter} com alerta de saúde",
                    Detail = d.OperationalStatus.Contains("Predictive", StringComparison.OrdinalIgnoreCase)
                        ? $"O disco {d.Model} reporta falha prevista pelo SMART: ele mesmo está avisando que pode parar de funcionar."
                        : $"O Windows reporta o disco {d.Model} como \"{d.Health}\" ({d.OperationalStatus}).",
                    Recommendation = "Faça backup dos arquivos desse disco agora e planeje a troca. Nenhum programa conserta um disco que está falhando: o que salva seus arquivos é a cópia.",
                    Evidence = evidence,
                    Actions =
                    [
                        new FindingAction("Fazer backup agora", "ms-settings:backup"),
                        new FindingAction("Ver saúde dos discos", "ms-settings:disksandvolumes"),
                    ],
                };
            }

            var lowSpace = d.FreePercent < 10 || d.FreeBytes < 15L * 1024 * 1024 * 1024;
            if (lowSpace && (d.IsSystemDrive || games.Count > 0))
            {
                anyIssue = true;
                yield return new Finding
                {
                    DiagnosticId = Id, Area = Areas.Storage, Status = d.IsSystemDrive ? HealthStatus.Problem : HealthStatus.Attention, ImpactArea = "SYSTEM",
                    Title = $"Pouco espaço livre em {d.DriveLetter}",
                    FixOptimizationId = d.IsSystemDrive ? "temp-files-cleanup" : null,
                    Detail = d.IsSystemDrive
                        ? "Com o disco do sistema quase cheio, o Windows perde espaço para paginação, atualizações e cache de shaders."
                        : $"Pouco espaço no disco dos jogos ({string.Join(", ", games)}). Atualizações podem falhar.",
                    Recommendation = "Use Configurações > Sistema > Armazenamento > Recomendações de limpeza, ou remova jogos que não usa.",
                    Evidence = evidence,
                    Actions = [new FindingAction("Abrir limpeza do Windows", "ms-settings:storagesense")],
                };
            }

            if (d.Media == MediaKind.Hdd && (d.IsSystemDrive || games.Count > 0))
            {
                anyIssue = true;
                yield return new Finding
                {
                    DiagnosticId = Id, Area = Areas.Storage, Status = HealthStatus.Attention, ImpactArea = "LOADING",
                    Title = games.Count > 0 ? $"{string.Join(", ", games)} instalado em HD mecânico" : $"Windows instalado em HD mecânico ({d.DriveLetter})",
                    Detail = "HD mecânico não reduz o FPS médio, mas aumenta tempo de carregamento e pode causar stutter quando o jogo carrega texturas durante a partida.",
                    Recommendation = "Mover o jogo (ou o Windows) para um SSD é a mudança que resolve. Nenhum tweak compensa a diferença.",
                    Evidence = evidence,
                };
            }
        }

        if (!anyIssue)
        {
            var system = s.Disks.FirstOrDefault(d => d.IsSystemDrive) ?? s.Disks[0];
            yield return new Finding
            {
                DiagnosticId = Id, Area = Areas.Storage, Status = HealthStatus.Ok,
                Title = $"Armazenamento: {(system.Media == MediaKind.Ssd ? "SSD" : system.Media == MediaKind.Hdd ? "HDD" : "tipo desconhecido")} {system.BusType}".TrimEnd(),
                Detail = "Espaço e saúde sem problemas nos discos do sistema e dos jogos.",
            };
        }
    }
}

public sealed class GpuDriverDiagnostic : IDiagnostic
{
    public string Id => "gpu-driver";

    // Links oficiais. O RKZFPS nunca baixa nem executa instalador de driver:
    // driver de fonte errada é o jeito mais fácil de quebrar ou infectar um
    // PC. O atualizador do próprio fabricante detecta o modelo e instala certo.
    private static string VendorUrl(GpuVendor v) => v switch
    {
        GpuVendor.Nvidia => "https://www.nvidia.com/pt-br/drivers/",
        GpuVendor.Amd => "https://www.amd.com/pt/support/download/drivers.html",
        GpuVendor.Intel => "https://www.intel.com.br/content/www/br/pt/support/detect.html",
        _ => "o site do fabricante da placa",
    };

    private static FindingAction? VendorUpdater(GpuVendor v) => v switch
    {
        GpuVendor.Nvidia => new FindingAction("Atualizar com o NVIDIA App", "https://www.nvidia.com/pt-br/software/nvidia-app/"),
        GpuVendor.Amd => new FindingAction("Atualizar com o AMD Adrenalin", "https://www.amd.com/pt/support/download/drivers.html"),
        GpuVendor.Intel => new FindingAction("Atualizar com o assistente da Intel", "https://www.intel.com.br/content/www/br/pt/support/detect.html"),
        _ => null,
    };

    public IEnumerable<Finding> Run(EvaluationContext context)
    {
        var s = context.Snapshot;
        if (s.Gpus.Count == 0)
        {
            yield return new Finding { DiagnosticId = Id, Area = Areas.Gpu, Status = HealthStatus.Unknown, Title = "GPU não identificada" };
            yield break;
        }

        var dedicated = s.Gpus.Where(g => !g.LikelyIntegrated).ToList();
        var main = dedicated.FirstOrDefault() ?? s.Gpus[0];
        var evidence = s.Gpus.ToDictionary(
            g => g.Name,
            g => $"driver {g.DriverVersion}, data {g.DriverDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "desconhecida"}, VRAM {(g.VramBytes is { } v ? Fmt.Gb(v) : "desconhecida")}");

        yield return new Finding
        {
            DiagnosticId = Id, Area = Areas.Gpu, Status = dedicated.Count == 0 ? HealthStatus.Info : HealthStatus.Ok,
            Title = main.Name,
            Detail = dedicated.Count == 0
                ? "Só foi encontrada GPU integrada. Jogos exigentes vão ficar limitados pela GPU, e isso não se resolve com otimização de software."
                : "GPU dedicada encontrada.",
            Evidence = evidence,
        };

        if (main.DriverDate is not { } date)
        {
            yield return new Finding { DiagnosticId = Id, Area = Areas.Driver, Status = HealthStatus.Unknown, Title = "Data do driver da GPU desconhecida", Evidence = evidence };
            yield break;
        }

        var age = s.CapturedAt.Date - date.Date;
        var limite = MaxDriverAgeDays(main);
        if (age.TotalDays > limite)
        {
            yield return new Finding
            {
                DiagnosticId = Id, Area = Areas.Driver, Status = HealthStatus.Attention, ImpactArea = "FPS",
                Title = "Driver da GPU potencialmente desatualizado",
                ActionUrl = main.Vendor == GpuVendor.Unknown ? null : VendorUrl(main.Vendor),
                Detail = $"O driver instalado é de {date:dd/MM/yyyy}, há mais de {(limite >= 365 ? "um ano" : "seis meses")}. Drivers novos costumam trazer correções e perfis para jogos recentes.",
                Recommendation = "Atualize pelo programa oficial do fabricante: ele identifica sua placa e instala o driver certo. Depois, rode a análise de novo para confirmar.",
                Evidence = evidence,
                Actions = new[] { VendorUpdater(main.Vendor), FindingAction.WindowsUpdateDrivers }.OfType<FindingAction>().ToList(),
            };
        }
        else
        {
            yield return new Finding
            {
                DiagnosticId = Id, Area = Areas.Driver, Status = HealthStatus.Ok,
                Title = $"Driver da GPU de {date:dd/MM/yyyy}", Detail = limite >= 365 ? "Driver com menos de um ano." : "Driver com menos de seis meses.", Evidence = evidence,
            };
        }
    }

    /// <summary>
    /// Placa dedicada NVIDIA ou AMD recebe driver quase todo mês, com perfil e
    /// correção para os jogos que acabaram de sair: seis meses já é atraso.
    /// Vídeo integrado e as demais marcas atualizam bem menos, e um ano segue valendo.
    /// </summary>
    public static int MaxDriverAgeDays(GpuInfo gpu) =>
        !gpu.LikelyIntegrated && gpu.Vendor is GpuVendor.Nvidia or GpuVendor.Amd ? 183 : 365;
}

/// <summary>
/// Integridade de memória (HVCI) tem custo de desempenho documentado pela
/// própria Microsoft em alguns jogos, mas é proteção do kernel. O RKZFPS mostra
/// a informação e não oferece desligar.
/// </summary>
public sealed class MemoryIntegrityDiagnostic : IDiagnostic
{
    public string Id => "memory-integrity-status";

    public IEnumerable<Finding> Run(EvaluationContext context)
    {
        var sec = context.Snapshot.Security;
        if (sec?.MemoryIntegrityEnabled is not { } enabled)
            yield break;

        yield return new Finding
        {
            DiagnosticId = Id, Area = Areas.Security, Status = enabled ? HealthStatus.Info : HealthStatus.Ok,
            Title = enabled ? "Integridade de memória ativada" : "Integridade de memória desativada",
            Detail = enabled
                ? "É um recurso de segurança do Windows que protege o kernel. Pode ter um custo pequeno de desempenho em alguns jogos. O RKZFPS não desativa recursos de segurança."
                : "Recurso de segurança do Windows desativado neste PC. O RKZFPS não altera essa configuração.",
            Evidence = new Dictionary<string, string> { ["hvci"] = enabled ? "ativada" : "desativada", ["vbs"] = sec.VbsRunning is { } v ? (v ? "rodando" : "parado") : "desconhecido" },
        };
    }
}
