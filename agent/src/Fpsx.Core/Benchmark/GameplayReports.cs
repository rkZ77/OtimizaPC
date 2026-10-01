using System.Globalization;
using System.Text;

namespace Fpsx.Core.Benchmark;

// Relatórios em cima das partidas já medidas: o resumo que aparece quando o
// jogo fecha, a meta de FPS por jogo e a planilha exportada. Nenhum deles mede
// nada novo nem estima número: tudo sai da partida gravada.

/// <summary>O aviso da bandeja quando uma partida termina.</summary>
public static class PostMatchSummary
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public static (string Title, string Text) For(GameplaySession s, int? goal = null)
    {
        var d = LimitAnalyzer.Diagnose(s);
        var sb = new StringBuilder(string.Format(Pt, "FPS médio {0:0}, 1% low {1:0}.", s.Stats.AvgFps, s.Stats.Low1Fps));
        if (goal is > 0 and var g)
            sb.Append(s.Stats.AvgFps >= g
                ? string.Format(Pt, " Bateu a meta de {0} FPS.", g)
                : string.Format(Pt, " Ficou abaixo da meta de {0} FPS.", g));
        // Sem dado suficiente, o aviso não inventa um culpado.
        if (d.Kind is not (LimitKind.Insufficient or LimitKind.Balanced))
            sb.Append(' ').Append(Sentence(d.Kind));
        sb.Append(" Veja os detalhes em Partidas.");
        return ($"{s.GameName}: partida registrada", sb.ToString());
    }

    public static string Sentence(LimitKind k) => k switch
    {
        LimitKind.Cpu => "O processador segurou o FPS.",
        LimitKind.Gpu => "A placa de vídeo segurou o FPS.",
        LimitKind.Ram => "Faltou memória RAM.",
        LimitKind.Vram => "Faltou memória de vídeo.",
        LimitKind.Temperature => "O PC esquentou e reduziu o clock.",
        LimitKind.Storage => "O disco segurou o jogo nas travadas.",
        LimitKind.Software => "Uma configuração ou programa segurou o FPS.",
        LimitKind.Balanced => "Nenhuma peça ficou no limite.",
        _ => "Sem dados suficientes para dizer o que limitou.",
    };
}

public sealed record FpsGoalResult(int Goal, int Matches, int Hits, LimitKind? MainLimit, int MainLimitCount, string Text);

/// <summary>Meta de FPS de um jogo: em quantas partidas bateu e o que mais impediu nas outras.</summary>
public static class FpsGoal
{
    public const int Min = 10;
    public const int Max = 1000;

    /// <summary>Partidas recentes que entram na conta (a meta vale para como o PC está agora).</summary>
    public const int Recent = 20;

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public static bool IsValid(int goal) => goal is >= Min and <= Max;

    /// <summary>Meta digitada: número inteiro na faixa; qualquer outra coisa é "sem meta".</summary>
    public static int? Parse(string? text) =>
        int.TryParse((text ?? "").Trim(), NumberStyles.Integer, Pt, out var v) && IsValid(v) ? v : null;

    public static FpsGoalResult Evaluate(IEnumerable<GameplaySession> gameSessions, int goal)
    {
        var recent = gameSessions.OrderByDescending(s => s.StartedAt).Take(Recent).ToList();
        if (recent.Count == 0)
            return new FpsGoalResult(goal, 0, 0, null, 0, string.Format(Pt, "Meta de {0} FPS. Jogue com o RKZFPS aberto para ver se ela é batida.", goal));

        // "Bater" é o FPS médio da partida alcançar a meta: é o número que a pessoa vê no jogo.
        var hits = recent.Count(s => s.Stats.AvgFps >= goal);
        var misses = recent.Where(s => s.Stats.AvgFps < goal)
            .Select(s => LimitAnalyzer.Diagnose(s).Kind)
            .Where(k => k is not (LimitKind.Insufficient or LimitKind.Balanced))
            .GroupBy(k => k)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        var text = string.Format(Pt, "Meta de {0} FPS: batida em {1} de {2} partida(s).", goal, hits, recent.Count);
        if (hits < recent.Count)
            text += misses is null
                ? " Nas que ficaram abaixo, não há leitura suficiente para dizer o que impediu."
                : string.Format(Pt, " Nas que ficaram abaixo, o que mais apareceu: {0} ({1} de {2}).",
                    Lower(PostMatchSummary.Sentence(misses.Key)), misses.Count(), recent.Count - hits);
        return new FpsGoalResult(goal, recent.Count, hits, misses?.Key, misses?.Count() ?? 0, text);
    }

    private static string Lower(string s) => s.Length > 0 ? char.ToLowerInvariant(s[0]) + s[1..].TrimEnd('.') : s;
}

/// <summary>
/// Histórico em planilha. Formato do Excel brasileiro: separador ";",
/// vírgula decimal e UTF-8 com BOM (sem o BOM, o Excel quebra os acentos).
/// Campo que não foi lido fica vazio, nunca zero.
/// </summary>
public static class GameplayCsv
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public static readonly string[] Header =
    [
        "Data", "Jogo", "Minutos medidos", "FPS médio", "1% low", "0.1% low", "Frametime médio (ms)", "Travadas por minuto",
        "CPU média (%)", "Núcleo mais usado (%)", "GPU média (%)", "Temperatura GPU média (°C)", "RAM média (%)",
        "Resolução", "Monitor (Hz)", "GPU usada", "O que limitou",
    ];

    public static string Write(IEnumerable<GameplaySession> sessions)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', Header));
        foreach (var s in sessions.OrderBy(s => s.StartedAt))
        {
            var h = SessionHealth.From(s);
            var d = LimitAnalyzer.Diagnose(s);
            sb.AppendLine(string.Join(';', new[]
            {
                s.StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Pt),
                s.GameName,
                N(s.MeasuredSeconds / 60, "0.0"),
                N(s.Stats.AvgFps, "0.0"),
                N(s.Stats.Low1Fps, "0.0"),
                N(s.Stats.Low01Fps, "0.0"),
                N(s.Stats.AvgFrametimeMs, "0.00"),
                N(s.Stats.StuttersPerMinute, "0.0"),
                N(h.CpuAvg ?? s.AvgCpuPercent, "0"),
                N(h.CpuMaxCoreAvg, "0"),
                N(h.GpuAvg ?? s.AvgGpuPercent, "0"),
                N(h.GpuTempAvg, "0"),
                N(h.RamAvg, "0"),
                s.ScreenWidth is { } w && s.ScreenHeight is { } hh ? $"{w}x{hh}" : "",
                s.DisplayHz?.ToString(Pt) ?? "",
                s.Gpu?.UsedGpu ?? "",
                d.Title,
            }.Select(Cell)));
        }

        return sb.ToString();
    }

    public static void Save(string path, IEnumerable<GameplaySession> sessions) =>
        File.WriteAllText(path, Write(sessions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    private static string N(double? v, string format) => v is { } x && double.IsFinite(x) ? x.ToString(format, Pt) : "";

    /// <summary>Aspas quando o texto tem separador, aspas ou quebra de linha (nome de jogo pode ter).</summary>
    private static string Cell(string v) =>
        v.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
}
