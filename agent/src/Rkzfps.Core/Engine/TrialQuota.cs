namespace Rkzfps.Core.Engine;

/// <summary>
/// Limite do teste grátis: até <see cref="MaxOptimizations"/> otimizações
/// diferentes neste PC. Pedido do dono (29/09/2026): a correção fica no PC
/// depois que o teste acaba, então um teste que aplica tudo substitui a
/// compra. O teste mostra o ganho de verdade (duas correções, medição, perfis
/// de jogo) e o resto fica para quem assina. Desfazer nunca é limitado.
/// </summary>
public sealed record TrialQuota(int Max, IReadOnlySet<string> Used)
{
    /// <summary>O site repete este número no texto do teste grátis.</summary>
    public const int MaxOptimizations = 2;

    public int Remaining => Math.Max(0, Max - Used.Count);

    /// <summary>
    /// Otimizações já aplicadas alguma vez neste PC. Desfazer não devolve a
    /// vaga: senão aplicar e desfazer em ciclo liberaria o catálogo inteiro.
    /// </summary>
    public static TrialQuota From(IEnumerable<SessionRecord> sessions) => new(MaxOptimizations,
        sessions.SelectMany(s => s.Changes)
            .Where(c => c.Status is ChangeStatus.Applied or ChangeStatus.RolledBack or ChangeStatus.RollbackFailed)
            .Select(c => c.OptimizationId)
            .Where(id => id.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase));

    /// <summary>Reaplicar uma que já usou a vaga (ligar de novo a chave) não gasta outra.</summary>
    public bool Allows(string optimizationId, ISet<string> takenNow) =>
        Used.Contains(optimizationId) || takenNow.Contains(optimizationId) || Used.Count + takenNow.Count < Max;

    public static readonly string SkipReason = $"No teste grátis o RKZFPS aplica até {MaxOptimizations} correções. Assine para aplicar esta.";
}
