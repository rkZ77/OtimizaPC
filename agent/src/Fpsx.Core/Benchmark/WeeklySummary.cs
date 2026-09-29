using System.Globalization;

namespace Fpsx.Core.Benchmark;

/// <summary>
/// Resumo da semana para a bandeja: o que foi MEDIDO nas partidas, sem
/// estimativa. Com poucas partidas não sai resumo (o número enganaria), e a
/// semana anterior só aparece com partidas suficientes do mesmo jogo.
/// </summary>
public static class WeeklySummary
{
    public const int MinMatches = 3;

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public static (string Title, string Text)? Build(IReadOnlyList<GameplaySession> all, DateTimeOffset now)
    {
        var week = all.Where(s => s.StartedAt > now.AddDays(-7) && s.StartedAt <= now).ToList();
        if (week.Count < MinMatches)
            return null;

        var hours = week.Sum(s => s.MeasuredSeconds) / 3600;
        var top = week.GroupBy(s => s.GameId).OrderByDescending(g => g.Count()).First().ToList();
        var text = string.Format(Pt, "{0} partidas e {1:0.#} h de jogo medidas.", week.Count, hours);
        if (top.Count >= MinMatches)
        {
            text += string.Format(Pt, " {0}: FPS médio {1:0} e pior 1% {2:0}", top[0].GameName,
                Median(top.Select(s => s.Stats.AvgFps)), Median(top.Select(s => s.Stats.Low1Fps)));
            var before = all.Where(s => s.GameId == top[0].GameId && s.StartedAt > now.AddDays(-14) && s.StartedAt <= now.AddDays(-7)).ToList();
            text += before.Count >= MinMatches
                ? string.Format(Pt, " (na semana anterior, {0:0} e {1:0}).", Median(before.Select(s => s.Stats.AvgFps)), Median(before.Select(s => s.Stats.Low1Fps)))
                : ".";
        }

        return ("Sua semana no RKZFPS", text + " Clique para ver as partidas.");
    }

    // Mediana: uma partida ruim (ou um mapa leve) não puxa o número da semana.
    private static double Median(IEnumerable<double> values)
    {
        var s = values.OrderBy(v => v).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }
}
