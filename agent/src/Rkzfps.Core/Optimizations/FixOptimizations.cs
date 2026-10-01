using System.Globalization;
using Rkzfps.Core.Diagnostics;
using Rkzfps.Core.Model;

namespace Rkzfps.Core.Optimizations;

// Correções para problemas que o diagnóstico ENCONTROU. Cada uma só existe
// quando o problema existe: em PC saudável todas dão NotApplicable ou
// AlreadyOptimal, e nada é oferecido.

/// <summary>Pagefile desativado (tweak popular e prejudicial) volta para "gerenciado pelo sistema".</summary>
public sealed class PagefileRestoreOptimization : IOptimization
{
    public string Id => "pagefile-restore-automatic";

    public const string SystemManaged = @"?:\pagefile.sys";

    public Evaluation Evaluate(EvaluationContext context)
    {
        var m = context.Snapshot.Memory;
        if (m?.PagefilePresent is null)
            return Evaluation.Unknown("Não foi possível ler a configuração de memória virtual.");
        if (m.PagefilePresent == true)
            return Evaluation.Optimal("O arquivo de paginação está ativo.");

        return new Evaluation
        {
            Decision = Decision.Recommended,
            Potential = Potential.Moderate,
            Reason = "O arquivo de paginação foi desativado. Sem ele, quando a memória acaba o jogo fecha em vez de só ficar lento.",
            Warning = "Exige reinício para valer.",
            Proposals =
            [
                new Proposal(Id, "Reativar o arquivo de paginação gerenciado pelo Windows",
                    [new RegistryValueChange(RegistryRoot.LocalMachine, RegistryPaths.MemoryManagement, "PagingFiles",
                        new RegValue(RegistryKind.MultiString, SystemManaged), NeedsReboot: true, Label: "Reativar o arquivo de paginação gerenciado pelo Windows")],
                    Potential.Moderate, "Evita fechamento do jogo por falta de memória."),
            ],
        };
    }
}

/// <summary>
/// Fecha, a pedido, programas do usuário que estão pesando agora: CPU alta em
/// segundo plano, ou RAM alta quando a memória já está apertada. Nunca
/// sistema, segurança, anti-cheat, launcher ou jogo (SafetyPolicy confere de novo).
/// </summary>
public sealed class BackgroundProcessCloseOptimization : IOptimization
{
    public string Id => "background-process-close";

    private const long HeavyRam = 1536L * 1024 * 1024;

    public Evaluation Evaluate(EvaluationContext context)
    {
        var s = context.Snapshot;
        if (s.Processes.Count == 0)
            return Evaluation.Unknown("Processos não amostrados.");

        var memoryTight = s.Memory is { } m && m.UsedPercent >= 80;
        // Um programa, não um processo: o Chrome tem dezenas, e o que pesa não é
        // o da janela. Soma tudo com o mesmo nome e manda o pedido de fechar
        // para o processo que tem janela. Sem janela não há como pedir com
        // educação, e o RKZFPS não força: então nem oferece.
        var candidates = s.Processes
            .Where(p => ProcessClassifier.Classify(p.Name) == ProcessCategory.User)
            .Where(p => !StartupClassifier.IsProtected(StartupClassifier.Classify(new StartupEntry { Name = p.Name })))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Window: g.FirstOrDefault(p => p.HasWindow), Cpu: g.Sum(p => p.CpuPercent), Ram: g.Sum(p => p.WorkingSetBytes)))
            .Where(x => x.Window is not null)
            .Where(x => x.Cpu >= 5 || (memoryTight && x.Ram >= HeavyRam))
            .OrderByDescending(x => x.Cpu).ThenByDescending(x => x.Ram)
            .Take(8)
            .Select(x => x.Window! with { CpuPercent = x.Cpu, WorkingSetBytes = x.Ram })
            .ToList();

        if (candidates.Count == 0)
            return Evaluation.Optimal("Nenhum programa em segundo plano pesando no momento do scan.");

        return new Evaluation
        {
            Decision = Decision.Recommended,
            Potential = Potential.Moderate,
            Reason = "Programas abertos estão disputando CPU ou memória com o jogo agora.",
            Warning = "O RKZFPS pede para o programa fechar, como o botão X. Salve seu trabalho antes. Se ele não fechar sozinho, nada é forçado.",
            Proposals = candidates.Select(p => new Proposal(
                $"{Id}:{p.Pid}",
                $"Fechar {p.Name} ({p.CpuPercent.ToString("0", CultureInfo.InvariantCulture)}% CPU, {p.WorkingSetBytes / 1024 / 1024} MB)",
                [new ProcessCloseChange(p.Pid, p.Name)],
                Potential.Moderate,
                "Libera CPU e memória para o jogo nesta sessão. O programa abre normalmente depois.")).ToList(),
        };
    }
}

/// <summary>Disco do sistema quase cheio: apaga temporários antigos do usuário.</summary>
public sealed class TempCleanupOptimization : IOptimization
{
    public string Id => "temp-files-cleanup";

    public Evaluation Evaluate(EvaluationContext context)
    {
        var system = context.Snapshot.Disks.FirstOrDefault(d => d.IsSystemDrive);
        if (system is null)
            return Evaluation.Unknown("Disco do sistema não identificado.");

        var low = system.FreePercent < 10 || system.FreeBytes < 15L * 1024 * 1024 * 1024;
        if (!low)
            return Evaluation.NotApplicable("Há espaço livre suficiente no disco do sistema.");

        return new Evaluation
        {
            Decision = Decision.Recommended,
            Potential = Potential.Low,
            Reason = $"O disco {system.DriveLetter} está quase cheio. Arquivos temporários antigos são o espaço mais seguro de recuperar.",
            Warning = "Arquivos temporários não voltam depois de apagados. Só são removidos os com mais de 24 horas e que não estão em uso.",
            Proposals = [new Proposal(Id, "Apagar arquivos temporários com mais de 24 horas", [new CacheClearChange(CacheTarget.UserTemp)], Potential.Low, "Libera espaço para paginação, atualizações e cache de shaders.")],
        };
    }
}

/// <summary>
/// Corrige no arquivo de vídeo do jogo o que o diagnóstico apontou (V-Sync,
/// Reflex, taxa de atualização abaixo do monitor). Só com o jogo fechado.
/// </summary>
public sealed class GameSettingsFixOptimization : IOptimization
{
    public string Id => "game-settings-fix";

    public Evaluation Evaluate(EvaluationContext context)
    {
        var s = context.Snapshot;
        var proposals = new List<Proposal>();
        var anyGame = false;

        foreach (var profile in context.GameProfiles)
        {
            var game = context.Game(profile.Id);
            if (game is null || game.Config.Count == 0)
                continue;
            anyGame = true;

            var vendors = s.Gpus.Where(g => !g.LikelyIntegrated).Select(g => g.Vendor).ToHashSet();
            foreach (var check in profile.SettingChecks.Where(c => c.FixValue is not null))
            {
                if (check.GpuVendor is { } vendor && !vendors.Contains(vendor))
                    continue;
                if (!game.Config.TryGetValue(check.Key, out var value) || check.ExpectAny.Contains(value, StringComparer.OrdinalIgnoreCase))
                    continue;
                proposals.Add(new Proposal($"{Id}:{profile.Id}:{check.Key}", $"{profile.Name}: {check.Title} (corrigir)",
                    [new GameConfigChange(profile.Id, check.Key, check.FixValue!)], Potential.Moderate, check.Why));
            }

            if (profile.RefreshRateCheck is { } rule
                && (s.Displays.FirstOrDefault(d => d.IsPrimary) ?? s.Displays.FirstOrDefault()) is { } display
                && int.TryParse(game.Config.GetValueOrDefault(rule.NumeratorKey), out var num)
                && int.TryParse(game.Config.GetValueOrDefault(rule.DenominatorKey), out var den) && den > 0
                && (int)Math.Round((double)num / den) + 1 < display.MaxHzAtCurrentResolution)
            {
                var hz = display.MaxHzAtCurrentResolution.ToString(CultureInfo.InvariantCulture);
                proposals.Add(new Proposal($"{Id}:{profile.Id}:refresh", $"{profile.Name}: usar {hz} Hz, a taxa do monitor",
                    [new GameConfigChange(profile.Id, rule.NumeratorKey, hz), new GameConfigChange(profile.Id, rule.DenominatorKey, "1")],
                    Potential.High, "O jogo estava configurado abaixo da taxa que o monitor suporta."));
            }
        }

        if (!anyGame)
            return Evaluation.NotApplicable("Nenhum jogo com perfil encontrado, ou o jogo ainda não criou a configuração de vídeo.");
        if (proposals.Count == 0)
            return Evaluation.Optimal("As configurações dos jogos já estão adequadas.");

        return new Evaluation
        {
            Decision = Decision.Recommended,
            Potential = Potential.Moderate,
            Reason = "O diagnóstico encontrou opções de vídeo do jogo que aumentam a latência ou limitam a taxa exibida.",
            Warning = "Feche o jogo antes de aplicar: ele reescreve o arquivo de configuração ao fechar.",
            Proposals = proposals,
        };
    }
}
