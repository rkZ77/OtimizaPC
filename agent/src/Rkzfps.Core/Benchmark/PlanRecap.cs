using System.Globalization;
using Rkzfps.Core.Engine;

namespace Rkzfps.Core.Benchmark;

/// <summary>Um ganho medido de verdade num jogo: só entra o que passou do limiar de ruído.</summary>
public sealed record GameGain(string GameName, string Metric, double Before, double After, double DeltaPercent);

/// <summary>
/// "O que o RKZFPS fez pelo seu PC", para a pessoa decidir a renovação com
/// fato na mão: as correções que seguem ativas e, por jogo, o antes e depois
/// das partidas medidas em volta da PRIMEIRA otimização (o PC como estava
/// antes do RKZFPS contra o PC de hoje).
///
/// Número só sai de medição: se a comparação não passou do limiar de ruído,
/// o resumo fala só das correções e não inventa ganho.
/// </summary>
public sealed record PlanRecap(int ActiveFixes, IReadOnlyList<string> FixNames, IReadOnlyList<GameGain> Gains)
{
    private static readonly string[] MetricsShown = ["FPS médio", "1% low"];

    public bool HasAnything => ActiveFixes > 0 || Gains.Count > 0;

    public static PlanRecap Build(IReadOnlyList<SessionRecord> history, IReadOnlyList<GameplaySession> games, Func<string, string?> optimizationName)
    {
        // Correção ativa: aplicada e não desfeita. Limpeza de cache e reparo
        // de rede não "ficam" no PC, então não contam como correção mantida.
        var active = history
            .SelectMany(s => s.Changes)
            .Where(c => c.Status == ChangeStatus.Applied && c.Applied is not (Model.CacheClearChange or Model.NetworkRepairChange))
            .ToList();
        var names = active.Select(c => optimizationName(c.OptimizationId) ?? c.OptimizationId)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var gains = new List<GameGain>();
        var first = GameplayComparer.Pivots(history).LastOrDefault();
        if (first is not null)
        {
            var clears = GameplayComparer.ShaderClears(history);
            foreach (var game in games.GroupBy(g => g.GameId))
            {
                var cmp = GameplayComparer.Compare(games, game.Key, game.First().GameName, first.StartedAt, "primeira otimização", clears);
                if (cmp.Result is not { } r)
                    continue;
                gains.AddRange(r.Metrics
                    .Where(m => m.Improved && MetricsShown.Contains(m.Metric))
                    .Select(m => new GameGain(cmp.GameName, m.Metric, m.Before, m.After, m.DeltaPercent)));
            }
        }

        return new PlanRecap(names.Count, names, gains);
    }

    /// <summary>Linhas curtas em pt-BR para a tela e o aviso da bandeja.</summary>
    public IReadOnlyList<string> Lines()
    {
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var lines = new List<string>();
        if (ActiveFixes > 0)
        {
            var sample = string.Join(", ", FixNames.Take(3));
            var more = FixNames.Count > 3 ? $" e mais {FixNames.Count - 3}" : "";
            lines.Add(ActiveFixes == 1
                ? $"1 correção ativa no seu PC: {sample}."
                : $"{ActiveFixes} correções ativas no seu PC: {sample}{more}.");
        }
        foreach (var g in Gains.Take(3))
            lines.Add($"{g.GameName}: {g.Metric} de {g.Before.ToString("0", pt)} para {g.After.ToString("0", pt)} ({(g.DeltaPercent >= 0 ? "+" : "")}{g.DeltaPercent.ToString("0", pt)}%), medido nas suas partidas.");
        return lines;
    }
}
