using Rkzfps.Core.Model;

namespace Rkzfps.Core.Engine;

/// <summary>Como uma otimização aparece no FPS Boost.</summary>
public enum BoostKind
{
    /// <summary>O RKZFPS aplicou e dá para desfazer: chave ligada, desligar desfaz.</summary>
    On,
    /// <summary>Resolveria algo neste PC com uma alteração só: chave desligada, ligar aplica.</summary>
    Off,
    /// <summary>Várias alterações possíveis (programas de inicialização, cada jogo): a pessoa escolhe quais.</summary>
    Pick,
    /// <summary>Já estava certo antes do RKZFPS: não há o que ligar nem desligar.</summary>
    AlreadyRight,
    /// <summary>Resolveria algo, mas pede outro plano.</summary>
    Locked,
    /// <summary>Reparo de uma vez só (limpar cache, rede): botão, não chave. Não aumenta FPS.</summary>
    Repair,
    /// <summary>Ação de uma vez só que ajuda agora (fechar programa pesando, apagar temporários): botão "Fazer agora".</summary>
    Action,
    /// <summary>Não se aplica a este PC.</summary>
    NotApplicable,
}

/// <summary>Uma alteração ativa no PC, com a sessão em que foi aplicada (para desfazer).</summary>
public sealed record ActiveChange(string SessionId, string ChangeId);

public sealed record BoostToggle(BoostKind Kind, IReadOnlyList<ActiveChange> Active, string? ProposalId);

/// <summary>
/// Traduz o resultado do motor para a chave ON/OFF do FPS Boost. A chave é
/// só a cara: ligar passa pelo mesmo fluxo de aplicar (confirmação, backup,
/// SafetyPolicy) e desligar pelo mesmo desfazer do Histórico.
/// </summary>
public static class BoostState
{
    public static BoostToggle For(OptimizationResult r, IReadOnlyList<SessionRecord> history)
    {
        var id = r.Definition.Id;
        // Ligada = o RKZFPS aplicou, a alteração segue no PC e tem volta.
        var active = history
            .SelectMany(s => s.Changes
                .Where(c => c.OptimizationId == id && c.Status == ChangeStatus.Applied && c.Inverse is not null)
                .Select(c => new ActiveChange(s.Id, c.Id)))
            .ToList();

        var repair = r.Definition.Category is "troubleshooting" or "network";
        var proposals = r.Evaluation.Proposals;
        // Sem volta (fechar programa, apagar arquivo): desligar a chave não
        // desfaria nada, então vira botão de uma vez só.
        var oneShot = proposals.Count > 0 && proposals.All(p => p.Changes.All(c => !c.Reversible));

        if (active.Count > 0 && !repair)
            return new BoostToggle(BoostKind.On, active, null);

        return r.Decision switch
        {
            Decision.Blocked => new BoostToggle(BoostKind.Locked, [], proposals.FirstOrDefault()?.Id),
            Decision.AlreadyOptimal => new BoostToggle(BoostKind.AlreadyRight, [], null),
            Decision.Recommended or Decision.Optional when repair && proposals.Count > 0 => new BoostToggle(BoostKind.Repair, [], proposals[0].Id),
            Decision.Recommended or Decision.Optional when oneShot => new BoostToggle(BoostKind.Action, [], proposals[0].Id),
            Decision.Recommended or Decision.Optional when proposals.Count == 1 => new BoostToggle(BoostKind.Off, [], proposals[0].Id),
            Decision.Recommended or Decision.Optional when proposals.Count > 1 => new BoostToggle(BoostKind.Pick, [], null),
            _ => new BoostToggle(BoostKind.NotApplicable, [], null),
        };
    }
}
