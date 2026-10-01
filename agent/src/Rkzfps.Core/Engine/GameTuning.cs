using Rkzfps.Core.Model;

namespace Rkzfps.Core.Engine;

/// <summary>
/// "Otimizar este jogo" e "Voltar como era", por jogo. O que liga é o
/// conjunto de propostas daquele jogo (correções de latência mais a
/// configuração pelo nível do PC); o que desliga é desfazer exatamente as
/// alterações que o RKZFPS gravou no arquivo daquele jogo, e nada de outro.
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

    /// <summary>Alterações do RKZFPS ainda valendo no arquivo do jogo, da mais nova para a mais antiga.</summary>
    public static IReadOnlyList<(string SessionId, string ChangeId, DateTimeOffset At)> AppliedChanges(IEnumerable<SessionRecord> sessions, string gameId) =>
        sessions
            .SelectMany(s => s.Changes.Select(c => (Session: s, Change: c)))
            .Where(x => x.Change.Status == ChangeStatus.Applied && x.Change.Applied is GameConfigChange g && g.GameId == gameId)
            .OrderByDescending(x => x.Change.At)
            .Select(x => (x.Session.Id, x.Change.Id, x.Change.At))
            .ToList();

    /// <summary>
    /// Perfis de gráficos do jogo para a tela Jogos: nome, se já é o que está
    /// no arquivo do jogo (todas as chaves do perfil iguais) e a proposta que
    /// aplica. Proposta nula com perfil ativo = nada a mudar.
    /// </summary>
    public static IReadOnlyList<GraphicsProfileOption> Profiles(ScanResult scan, Games.GameProfile profile)
    {
        var install = scan.Snapshot.Games.FirstOrDefault(g => g.GameId == profile.Id);
        if (install is null || install.Config.Count == 0)
            return [];
        var proposals = scan.Optimizations
            .Where(o => o.Definition.Id == "game-graphics-profile")
            .SelectMany(o => o.Evaluation.Proposals)
            .Select(p => p.Id)
            .ToHashSet();
        return profile.Presets.Select(preset =>
        {
            var keys = preset.Settings.Where(kv => install.Config.ContainsKey(kv.Key)).ToList();
            var active = keys.Count > 0 && keys.All(kv => install.Config[kv.Key] == kv.Value);
            var id = Optimizations.GraphicsProfileOptimization.ProposalId(profile.Id, preset.Id);
            return new GraphicsProfileOption(preset.Id, Optimizations.GraphicsProfileOptimization.Label(preset), preset.Description,
                proposals.Contains(id) ? id : null, active);
        }).ToList();
    }
}

public sealed record GraphicsProfileOption(string PresetId, string Label, string Description, string? ProposalId, bool IsActive);
