using Fpsx.Core.Model;

namespace Fpsx.Core.Engine;

/// <summary>
/// "Otimizar este jogo" e "Voltar como era", por jogo. O que liga é o
/// conjunto de propostas daquele jogo (correções de latência mais a
/// configuração pelo nível do PC); o que desliga é desfazer exatamente as
/// alterações que o FPSX gravou no arquivo daquele jogo, e nada de outro.
/// </summary>
public static class GameTuning
{
    private static readonly string[] Sources = ["game-settings-fix", "game-preset-low-end"];

    /// <summary>Propostas aplicáveis agora para o jogo (pode ser vazio: já está certo).</summary>
    public static IReadOnlyList<string> ProposalIdsFor(ScanResult scan, string gameId) =>
        scan.Optimizations
            .Where(o => Sources.Contains(o.Definition.Id))
            .SelectMany(o => o.Evaluation.Proposals.Select(p => (o, p)))
            .Where(x => x.p.Id.StartsWith($"{x.o.Definition.Id}:{gameId}:", StringComparison.Ordinal))
            .Select(x => x.p.Id)
            .ToList();

    /// <summary>Alterações do FPSX ainda valendo no arquivo do jogo, da mais nova para a mais antiga.</summary>
    public static IReadOnlyList<(string SessionId, string ChangeId, DateTimeOffset At)> AppliedChanges(IEnumerable<SessionRecord> sessions, string gameId) =>
        sessions
            .SelectMany(s => s.Changes.Select(c => (Session: s, Change: c)))
            .Where(x => x.Change.Status == ChangeStatus.Applied && x.Change.Applied is GameConfigChange g && g.GameId == gameId)
            .OrderByDescending(x => x.Change.At)
            .Select(x => (x.Session.Id, x.Change.Id, x.Change.At))
            .ToList();
}
