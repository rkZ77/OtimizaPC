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
    }

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

public sealed class OptimizationItem
{
    public OptimizationItem(OptimizationResult r)
    {
        Result = r;
        var (label, brush) = StatusStyle.Of(r.Decision);
        DecisionLabel = label;
        DecisionBrush = StatusStyle.Brush(brush);
        var actionable = r.Decision is Decision.Recommended or Decision.Optional;
        Proposals = r.Evaluation.Proposals.Select(p => new ProposalItem(p, actionable) { IsSelected = actionable && r.AutoSelected }).ToList();
    }

    public OptimizationResult Result { get; }
    public string Id => Result.Definition.Id;
    public string Name => Result.Definition.Name;
    public string Meta => $"{Result.Definition.Classification.ToString().ToUpperInvariant()} · risco {Risk(Result.Definition.Risk)} · {StatusStyle.Potential(Result.Evaluation.Potential)}";
    public string Reason => Result.Reason;
    public string? Warning => Result.Evaluation.Warning;
    public string DecisionLabel { get; }
    public Brush DecisionBrush { get; }
    public IReadOnlyList<ProposalItem> Proposals { get; }
    public bool HasProposals => Proposals.Count > 0 && Result.Decision is Decision.Recommended or Decision.Optional;
    public bool IsBlocked => Result.Decision == Decision.Blocked;

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
    public string Title { get; } = $"{s.StartedAt.ToLocalTime():dd/MM/yyyy HH:mm} · perfil {s.ProfileId}";
    public string Summary { get; } =
        $"{s.Changes.Count(c => c.Status == ChangeStatus.Applied)} ativa(s), {s.Changes.Count(c => c.Status == ChangeStatus.RolledBack)} desfeita(s), " +
        $"{s.AlreadyOptimal.Count} já estavam corretas, {s.Skipped.Count} ignorada(s)";
    public IReadOnlyList<ChangeItem> Changes { get; } = s.Changes.Select(c => new ChangeItem(s.Id, c)).ToList();
    public bool CanUndo => Changes.Any(c => c.CanUndo);
}
