using System.Windows.Media;
using Fpsx.Core.Diagnostics;
using Fpsx.Core.Engine;
using Fpsx.Core.Model;
using Fpsx.Core.Optimizations;

namespace Fpsx.App.ViewModels;

public sealed record KeyValueItem(string Key, string Value);

public sealed class ReadinessItem(string area, HealthStatus status, string summary)
{
    public string Area { get; } = area;
    public string Summary { get; } = summary;
    public string StatusLabel { get; } = StatusStyle.Of(status).Label;
    public Brush StatusBrush { get; } = StatusStyle.Brush(StatusStyle.Of(status).Brush);
}

/// <summary>Um problema encontrado, com o botão Resolver quando existe correção.</summary>
public sealed class FindingItem
{
    public FindingItem(Finding f, ScanResult scan)
    {
        Finding = f;
        var (label, brush) = StatusStyle.Of(f.Status);
        StatusLabel = label;
        StatusBrush = StatusStyle.Brush(brush);

        var fix = f.FixOptimizationId is null ? null : scan.Optimizations.FirstOrDefault(o => o.Definition.Id == f.FixOptimizationId);
        if (fix is not null && fix.Evaluation.Proposals.Count > 0)
        {
            FixId = fix.Definition.Id;
            FixBlockedReason = fix.Decision == Decision.Blocked ? fix.Reason : null;
        }

        var impact = f.Impact ?? (fix is not null && fix.Evaluation.Potential != Potential.None ? fix.Evaluation.Potential : null);
        ImpactText = impact is { } p ? StatusStyle.Impact(p) : null;
    }

    /// <summary>"Pode fazer diferença grande no FPS": o efeito esperado, sem número.</summary>
    public string? ImpactText { get; }

    public Finding Finding { get; }
    public string Title => Finding.Title;
    public string Detail => Finding.Detail;
    public string? Recommendation => Finding.Recommendation is null ? null : "Recomendação: " + Finding.Recommendation;
    public string StatusLabel { get; }
    public Brush StatusBrush { get; }
    public string? FixId { get; }
    public string? FixBlockedReason { get; }
    public bool HasFix => FixId is not null;
    public string FixLabel => FixBlockedReason is null ? "Resolver" : "Resolver (bloqueado)";
    public string? ActionUrl => Finding.ActionUrl;

    /// <summary>Ações externas (driver, backup, espaço). Só as permitidas viram botão.</summary>
    public IReadOnlyList<FindingAction> Actions => Finding.Actions.Where(a => a.IsAllowed).ToList();
}

public sealed class ProposalItem(Proposal p, bool actionable) : ObservableObject
{
    private bool _selected;

    public string Id { get; } = p.Id;
    public string Title { get; } = p.Title;
    public string Changes { get; } = string.Join("\n", p.Changes.Select(c => "• " + c.Describe()
        + (c.Reversible ? "" : " (sem desfazer)") + (c.RequiresReboot ? " (exige reinício)" : "")));
    public bool Actionable { get; } = actionable;

    public bool IsSelected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }
}

public sealed class OptimizationItem : ObservableObject
{
    public OptimizationItem(OptimizationResult r, IReadOnlyList<SessionRecord>? history = null)
    {
        Result = r;
        Boost = BoostState.For(r, history ?? []);
        var (label, brush) = StatusStyle.Of(r.Decision);
        DecisionLabel = label;
        DecisionBrush = StatusStyle.Brush(brush);
        var actionable = r.Decision is Decision.Recommended or Decision.Optional;
        Proposals = r.Evaluation.Proposals.Select(p => new ProposalItem(p, actionable) { IsSelected = actionable && r.AutoSelected }).ToList();
    }

    public OptimizationResult Result { get; }
    public string Id => Result.Definition.Id;
    public string Name => Result.Definition.Name;
    public string Meta => $"{Kind(Result.Definition.Classification)}, risco {Risk(Result.Definition.Risk)}, {StatusStyle.Potential(Result.Evaluation.Potential).ToLowerInvariant()}"
                          + (Result.RequiresElevation ? ", pede permissão do Windows" : "");

    private static string Kind(Classification c) => c switch
    {
        Classification.Proven => "Comprovada",
        Classification.Conditional => "Depende do PC",
        Classification.Troubleshooting => "Solução de problema",
        Classification.Experimental => "Experimental",
        _ => "Não recomendada",
    };
    public string Reason => Result.Reason;
    public string? Warning => Result.Evaluation.Warning;
    public string DecisionLabel { get; }
    public Brush DecisionBrush { get; }
    public IReadOnlyList<ProposalItem> Proposals { get; }
    public bool HasProposals => Proposals.Count > 0 && Result.Decision is Decision.Recommended or Decision.Optional;
    public bool IsBlocked => Result.Decision == Decision.Blocked;

    // ---- FPS Boost: a chave ON/OFF de cada otimização ----

    public BoostToggle Boost { get; }
    public bool IsOn => Boost.Kind == BoostKind.On;

    /// <summary>Chave de ligar e desligar (ligada, desligada ou presa no plano).</summary>
    public bool ShowSwitch => Boost.Kind is BoostKind.On or BoostKind.Off or BoostKind.Locked;
    public bool IsLocked => Boost.Kind == BoostKind.Locked;
    public bool ShowPick => Boost.Kind == BoostKind.Pick;
    public bool ShowRepair => Boost.Kind is BoostKind.Repair;
    public bool ShowAction => Boost.Kind is BoostKind.Action;

    /// <summary>Selo do plano que libera, no lugar do "trava" genérico (como a estrela da GC).</summary>
    public string PlanBadge => Result.Definition.MinPlan switch
    {
        "starter" => "Starter",
        "pro" => "Pro",
        "ultimate" => "Ultimate",
        _ => "Plano",
    };

    private bool _picking;

    /// <summary>Lista de itens aberta (para as otimizações com mais de uma alteração).</summary>
    public bool Picking
    {
        get => _picking;
        set => Set(ref _picking, value);
    }

    private static string Risk(RiskLevel r) => r switch { RiskLevel.Low => "baixo", RiskLevel.Medium => "médio", _ => "alto" };
}

public sealed class ChangeItem(string sessionId, ChangeRecord c)
{
    public string SessionId { get; } = sessionId;
    public string Id { get; } = c.Id;
    public string Description { get; } = c.Applied.Describe();
    public string StatusLabel { get; } = c.Status switch
    {
        ChangeStatus.Applied => "ATIVA",
        ChangeStatus.RolledBack => "DESFEITA",
        ChangeStatus.RollbackSkipped => "MANTIDA",
        ChangeStatus.Failed => "FALHOU",
        ChangeStatus.RollbackFailed => "FALHA AO DESFAZER",
        _ => "PENDENTE",
    };
    public string? Error { get; } = c.Error;
    public bool CanUndo { get; } = c.Status == ChangeStatus.Applied && c.Inverse is not null;
}

public sealed class SessionItem(SessionRecord s)
{
    public SessionRecord Session { get; } = s;
    public string Title { get; } = $"{s.StartedAt.ToLocalTime():dd/MM/yyyy HH:mm}, perfil {s.ProfileId}";
    public string Summary { get; } =
        $"{s.Changes.Count(c => c.Status == ChangeStatus.Applied)} ativa(s), {s.Changes.Count(c => c.Status == ChangeStatus.RolledBack)} desfeita(s), " +
        $"{s.AlreadyOptimal.Count} já estavam corretas, {s.Skipped.Count} ignorada(s)";
    public IReadOnlyList<ChangeItem> Changes { get; } = s.Changes.Select(c => new ChangeItem(s.Id, c)).ToList();
    public bool CanUndo => Changes.Any(c => c.CanUndo);
}

/// <summary>
/// Programa ou jogo aberto que pesa agora. Mostra quanto pesa em palavras e
/// se a pessoa nem está usando (minimizado). Só tem botão de fechar quando o
/// RKZFPS pode pedir com segurança (programa do usuário com janela); jogo e
/// launcher a pessoa fecha, porque pode estar no meio de algo.
/// </summary>
public sealed record OpenAppItem(string Name, string Kind, string Detail, string? ProposalId)
{
    public bool CanClose => ProposalId is not null;

    public string Initials => GameIcons.Initials(Name);

    public System.Windows.Media.ImageSource? Icon { get; init; }

    public string Hint => CanClose ? "" : Kind switch
    {
        "Jogo" => "Feche pelo próprio jogo se não estiver jogando.",
        "Launcher" => "Feche pelo ícone perto do relógio se não for jogar agora.",
        _ => "Se não estiver usando, feche antes de jogar.",
    };

    /// <summary>Ícone do próprio programa aberto (o mesmo da barra de tarefas), guardado como os dos jogos.</summary>
    private static System.Windows.Media.ImageSource? IconOf(string processName, int pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return p.MainModule?.FileName is { } exe ? GameIcons.Get("app-" + processName.ToLowerInvariant(), exe) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Programa protegido ou que já fechou: fica com as iniciais.
            return GameIcons.Get("app-" + processName.ToLowerInvariant());
        }
    }

    /// <summary>Acima disto o programa entra na lista: abaixo, o peso não aparece no jogo.</summary>
    public const double MinCpuPercent = 3;

    public const long MinRamBytes = 600L * 1024 * 1024;

    public static IReadOnlyList<OpenAppItem> From(ScanResult scan, IReadOnlyList<Fpsx.Core.Games.GameProfile> games)
    {
        var closable = scan.Optimizations.FirstOrDefault(o => o.Definition.Id == "background-process-close")?.Evaluation.Proposals
            .Select(p => p.Id).ToHashSet() ?? [];
        var gameByProcess = games
            .SelectMany(g => g.Detect.Processes.Select(p => (Process: p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p, g.Name)))
            .GroupBy(x => x.Process, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
        var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

        return scan.Snapshot.Processes
            .Where(p => !p.Name.StartsWith("fpsx", StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var window = g.FirstOrDefault(p => p.HasWindow);
                var category = ProcessClassifier.Classify(g.Key);
                return (g.Key, Window: window, Category: category, Cpu: g.Sum(p => p.CpuPercent), Ram: g.Sum(p => p.WorkingSetBytes));
            })
            .Where(x => x.Window is not null && x.Category is ProcessCategory.User or ProcessCategory.Game or ProcessCategory.Launcher)
            .Where(x => x.Cpu >= MinCpuPercent || x.Ram >= MinRamBytes)
            .OrderByDescending(x => x.Cpu + x.Ram / (256.0 * 1024 * 1024))
            .Take(6)
            .Select(x =>
            {
                var isGame = x.Category == ProcessCategory.Game || gameByProcess.ContainsKey(x.Key);
                var name = gameByProcess.TryGetValue(x.Key, out var gameName) ? gameName : char.ToUpperInvariant(x.Key[0]) + x.Key[1..];
                var detail = string.Format(pt, "{0:0}% do processador e {1:0.0} GB de memória{2}", x.Cpu, x.Ram / (1024.0 * 1024 * 1024),
                    x.Window!.Minimized ? ", minimizado: aberto sem uso" : "");
                var proposal = $"background-process-close:{x.Window.Pid}";
                return new OpenAppItem(name, isGame ? "Jogo" : x.Category == ProcessCategory.Launcher ? "Launcher" : "Programa", detail,
                    closable.Contains(proposal) ? proposal : null)
                {
                    Icon = IconOf(x.Key, x.Window.Pid),
                };
            })
            .ToList();
    }
}
