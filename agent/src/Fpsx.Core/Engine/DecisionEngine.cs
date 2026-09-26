using Fpsx.Core.Catalog;
using Fpsx.Core.Diagnostics;
using Fpsx.Core.Games;
using Fpsx.Core.Model;
using Fpsx.Core.Optimizations;

namespace Fpsx.Core.Engine;

public static class Plans
{
    public static readonly string[] Order = ["free", "starter", "pro", "ultimate"];

    public static int Rank(string plan)
    {
        var i = Array.FindIndex(Order, p => string.Equals(p, plan, StringComparison.OrdinalIgnoreCase));
        return i < 0 ? 0 : i;
    }

    public static string Label(string plan) => plan.ToLowerInvariant() switch
    {
        "starter" => "Starter",
        "pro" => "Pro",
        "ultimate" => "Ultimate",
        _ => "Free",
    };
}

public sealed record OptimizationResult
{
    public OptimizationDefinition Definition { get; init; } = new();
    public Evaluation Evaluation { get; init; } = new();

    /// <summary>Decisão final depois de catálogo, admin, plano e perfil.</summary>
    public Decision Decision { get; init; }

    public string Reason { get; init; } = "";

    /// <summary>Selecionada pelo perfil sem precisar de pergunta individual.</summary>
    public bool AutoSelected { get; init; }

    /// <summary>
    /// Altera configuração do sistema e o FPSX está sem administrador. Não é
    /// bloqueio: o app pede a permissão do Windows na hora de aplicar.
    /// </summary>
    public bool RequiresElevation { get; init; }
}

public sealed record ScanResult
{
    public SystemSnapshot Snapshot { get; init; } = new();
    public IReadOnlyList<Finding> Findings { get; init; } = [];
    public IReadOnlyList<OptimizationResult> Optimizations { get; init; } = [];
    public string CatalogVersion { get; init; } = "";
    public string ProfileId { get; init; } = "";
    public string Plan { get; init; } = "free";

    public (OptimizationResult Result, Proposal Proposal)? FindProposal(string proposalId)
    {
        foreach (var r in Optimizations)
            foreach (var p in r.Evaluation.Proposals)
                if (string.Equals(p.Id, proposalId, StringComparison.OrdinalIgnoreCase))
                    return (r, p);
        return null;
    }
}

/// <summary>
/// Detectar, validar compatibilidade, verificar necessidade, avaliar risco:
/// a parte do fluxo da seção 25 que acontece antes de qualquer backup. Não
/// altera nada no sistema.
/// </summary>
public sealed class DecisionEngine(
    OptimizationCatalog catalog,
    IReadOnlyList<GameProfile> gameProfiles,
    IReadOnlyList<IOptimization>? handlers = null,
    IReadOnlyList<IDiagnostic>? diagnostics = null)
{
    private readonly IReadOnlyList<IOptimization> _handlers = handlers ?? OptimizationRegistry.All;
    private readonly IReadOnlyList<IDiagnostic> _diagnostics = diagnostics ?? DiagnosticRegistry.All;

    public ScanResult Evaluate(SystemSnapshot snapshot, string profileId, string plan)
    {
        var errors = CatalogValidator.Validate(catalog, _handlers);
        if (errors.Count > 0)
            throw new InvalidDataException("Catálogo de otimizações inválido:\n" + string.Join("\n", errors));

        var profile = catalog.FindProfile(profileId) ?? throw new ArgumentException($"Perfil desconhecido: {profileId}");
        var context = new EvaluationContext(snapshot, gameProfiles);
        var enabledDiagnostics = catalog.Diagnostics.Where(d => d.Enabled).Select(d => d.Id).ToHashSet();

        var findings = _diagnostics
            .Where(d => catalog.Diagnostics.Count == 0 || enabledDiagnostics.Contains(d.Id))
            .SelectMany(d => SafeRun(d, context))
            .ToList();

        var results = _handlers
            .Select(h => (Handler: h, Definition: catalog.Find(h.Id)!))
            .Select(x => Decide(x.Handler, x.Definition, context, profile, plan))
            .ToList();

        return new ScanResult
        {
            Snapshot = snapshot,
            Findings = findings,
            Optimizations = results,
            CatalogVersion = catalog.Version,
            ProfileId = profile.Id,
            Plan = plan,
        };
    }

    // Um diagnóstico que quebra vira Unknown no relatório, e o scan continua.
    private static IEnumerable<Finding> SafeRun(IDiagnostic diagnostic, EvaluationContext context)
    {
        try
        {
            return diagnostic.Run(context).ToList();
        }
        catch (Exception ex)
        {
            return [new Finding { DiagnosticId = diagnostic.Id, Area = "FPSX", Status = HealthStatus.Unknown, Title = $"Falha no diagnóstico {diagnostic.Id}", Detail = ex.Message }];
        }
    }

    private static OptimizationResult Decide(IOptimization handler, OptimizationDefinition def, EvaluationContext context, ProfileDefinition profile, string plan)
    {
        Evaluation evaluation;
        try
        {
            evaluation = handler.Evaluate(context);
        }
        catch (Exception ex)
        {
            evaluation = Evaluation.Unknown($"Falha ao avaliar: {ex.Message}");
        }

        var result = new OptimizationResult { Definition = def, Evaluation = evaluation, Decision = evaluation.Decision, Reason = evaluation.Reason };
        if (evaluation.Decision is not (Decision.Recommended or Decision.Optional))
            return result;

        // Ordem importa: o motivo de bloqueio mostrado é o primeiro que pega.
        if (!def.Enabled)
            return result with { Decision = Decision.Blocked, Reason = "Desativada pelo administrador do FPSX." };
        // O motivo do handler fica: no Free a pessoa vê o problema e o que a
        // otimização resolveria, e só a aplicação depende do plano.
        if (Plans.Rank(plan) < Plans.Rank(def.MinPlan))
            return result with { Decision = Decision.Blocked, Reason = $"{evaluation.Reason} Para aplicar: plano {Plans.Label(def.MinPlan)}." };
        if (!context.Snapshot.IsElevated && evaluation.Proposals.SelectMany(p => p.Changes).Any(c => c.RequiresAdmin))
            result = result with { RequiresElevation = true };

        // EXPERIMENTAL e TROUBLESHOOTING nunca são "recomendadas": ficam
        // disponíveis por escolha, sem entrar em seleção automática.
        if (def.Classification is Classification.Experimental or Classification.Troubleshooting && result.Decision == Decision.Recommended)
            result = result with { Decision = Decision.Optional };

        var inProfile = (profile.Include.Count == 0 || profile.Include.Contains(def.Id)) && !profile.Exclude.Contains(def.Id);
        var auto = result.Decision == Decision.Recommended
                   && inProfile
                   && profile.AutoClassifications.Contains(def.Classification)
                   && def.Risk <= profile.MaxRisk
                   && !def.RequiresUserConfirmation;

        return result with { AutoSelected = auto };
    }
}
