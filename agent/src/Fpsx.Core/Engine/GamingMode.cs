using Fpsx.Core.Model;

namespace Fpsx.Core.Engine;

// Modo Gaming. Manual: nada muda sozinho, a pessoa aplica pelas telas de
// sempre. Automático: quando um jogo abre, aplica SÓ o que a pessoa autorizou
// antes, e desfaz quando o jogo fecha. Não é "aplicar tudo": o filtro abaixo
// deixa passar apenas alterações de risco baixo, reversíveis, sem
// administrador, sem reinício e com efeito na hora (hoje, o plano de energia).
// Fechar programa, mexer em arquivo do jogo aberto, instalar driver ou mudar a
// tela nunca entram: ou não têm volta, ou não valem com o jogo já aberto, ou
// piscam a tela no meio da partida.

public enum GamingModeKind
{
    Manual,
    Automatic,
}

/// <summary>Uma otimização que pode ser autorizada para o modo Automático.</summary>
public sealed record GamingCandidate(string OptimizationId, string Name, string Reason, bool ApplicableNow, bool Authorized);

public sealed record GamingOutcome(bool Applied, SessionRecord? Session, string Message);

public static class GamingPolicy
{
    public static GamingModeKind Parse(string? value) =>
        string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase) ? GamingModeKind.Automatic : GamingModeKind.Manual;

    public static string Serialize(GamingModeKind mode) => mode == GamingModeKind.Automatic ? "auto" : "manual";

    /// <summary>Tipo de alteração que dá para ligar ao abrir o jogo e desligar ao fechar sem efeito colateral.</summary>
    public static bool IsTemporaryChange(Change c) =>
        c is PowerSchemeChange && c.Reversible && !c.RequiresAdmin && !c.RequiresReboot;

    /// <summary>A otimização, pelo que ela é, pode ser autorizada para o Automático (independe do estado atual do PC).</summary>
    public static bool IsEligible(OptimizationResult r) =>
        r.Definition.Classification is Classification.Proven or Classification.Conditional
        && r.Definition.Risk == RiskLevel.Low
        && r.Evaluation.Proposals.Count > 0
        && r.Evaluation.Proposals.All(p => p.Changes.Count > 0 && p.Changes.All(IsTemporaryChange));

    /// <summary>Aplicável agora: elegível, recomendada ou opcional neste PC e sem pedir administrador.</summary>
    public static bool ApplicableNow(OptimizationResult r) =>
        IsEligible(r) && r.Decision is Decision.Recommended or Decision.Optional && !r.RequiresElevation;

    /// <summary>Otimizações que aparecem na lista de autorização, com o estado de agora.</summary>
    public static IReadOnlyList<GamingCandidate> Candidates(ScanResult scan, IReadOnlyCollection<string> authorized) =>
        scan.Optimizations
            .Where(IsEligible)
            .Select(r => new GamingCandidate(r.Definition.Id, r.Definition.Name, r.Evaluation.Reason, ApplicableNow(r),
                authorized.Contains(r.Definition.Id, StringComparer.OrdinalIgnoreCase)))
            .ToList();

    /// <summary>Propostas que o Automático aplica agora: autorizadas E aplicáveis. Nada fora disso.</summary>
    public static IReadOnlyList<string> ProposalsToApply(ScanResult scan, IReadOnlyCollection<string> authorized) =>
        scan.Optimizations
            .Where(r => authorized.Contains(r.Definition.Id, StringComparer.OrdinalIgnoreCase) && ApplicableNow(r))
            .SelectMany(r => r.Evaluation.Proposals.Select(p => p.Id))
            .ToList();
}

/// <summary>
/// Liga e desliga as alterações temporárias. Usa o mesmo motor de sempre
/// (backup, SafetyPolicy, verificação, histórico) e o mesmo desfazer do Histórico.
/// </summary>
public sealed class GamingSessionManager(ISystemAccess system, SessionStore store)
{
    /// <summary>Sessões temporárias com alteração ainda valendo (a do jogo aberto, ou sobra de um app que fechou no meio).</summary>
    public IReadOnlyList<SessionRecord> Pending() =>
        store.All()
            .Where(s => s.GamingGame is not null && s.Changes.Any(c => c.Status is ChangeStatus.Applied or ChangeStatus.Pending && c.Inverse is not null))
            .ToList();

    public GamingOutcome Start(string gameName, ScanResult scan, GamingModeKind mode, IReadOnlyCollection<string> authorized, TrialQuota? trial = null)
    {
        if (mode == GamingModeKind.Manual)
            return new GamingOutcome(false, null, "Modo Manual: nada é aplicado sozinho.");

        // Sobra de uma partida anterior (app fechou no meio): volta antes de começar outra.
        Restore();

        var ids = GamingPolicy.ProposalsToApply(scan, authorized);
        if (ids.Count == 0)
            return new GamingOutcome(false, null, authorized.Count == 0
                ? $"{gameName}: modo Automático sem nenhuma otimização autorizada. Nada foi alterado."
                : $"{gameName}: as otimizações autorizadas já estão valendo neste PC. Nada foi alterado.");

        // Falha no meio da partida não abre diálogo: desfaz o que já entrou e
        // o PC fica como estava (estado consistente, sem meio-termo).
        var session = new OptimizationEngine(system, store).Apply(scan, ids, new ApplyOptions
        {
            OnFailure = _ => FailureChoice.Restore,
            Trial = trial,
            GamingGame = gameName,
        });

        var applied = session.Changes.Count(c => c.Status == ChangeStatus.Applied);
        if (session.Status == SessionStatus.RolledBack)
            return new GamingOutcome(false, session, $"{gameName}: uma alteração falhou, e o que tinha entrado foi desfeito. O PC ficou como estava.");
        return applied > 0
            ? new GamingOutcome(true, session, $"{gameName}: {applied} alteração(ões) temporária(s) aplicada(s). Tudo volta quando o jogo fechar.")
            : new GamingOutcome(false, session, $"{gameName}: nada foi aplicado ({string.Join("; ", session.Skipped.Select(s => s.Reason))}).");
    }

    /// <summary>
    /// Desfaz as sessões temporárias. Item que a pessoa (ou outro programa)
    /// mudou durante o jogo não é sobrescrito: fica como ela deixou, igual ao
    /// desfazer do Histórico.
    /// </summary>
    public IReadOnlyList<SessionRecord> Restore() =>
        Pending().Select(s =>
        {
            // "Pendente" = o app caiu entre guardar o backup e confirmar a
            // alteração: ela pode ter entrado ou não. O backup já foi guardado,
            // então trata como aplicada; se não entrou, o desfazer vê que o
            // valor não é o do RKZFPS e não mexe.
            var fixedUp = s with { Changes = s.Changes.Select(c => c.Status == ChangeStatus.Pending && c.Inverse is not null ? c with { Status = ChangeStatus.Applied } : c).ToList() };
            return new RollbackManager(system, store).RollbackSession(fixedUp, force: false);
        }).ToList();
}
