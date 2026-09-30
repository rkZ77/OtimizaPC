namespace Fpsx.Core.Benchmark;

/// <summary>O que foi medido nas partidas de um jogo, em números que a IA consegue ler.</summary>
public sealed record GameEvidence(
    string GameId, string Game, int Matches, double AvgFps, double Low1Fps, int? DisplayHz,
    double? AvgCpu, double? AvgGpu, int Drops, int GpuDrops, int CpuDrops, int AppDrops, LimitKind? Diagnosed = null);

/// <summary>Onde o PC trava o FPS, pelo que foi medido.</summary>
public enum Bottleneck
{
    /// <summary>Placa de vídeo no limite na maior parte das quedas (ou no uso médio).</summary>
    Gpu,
    /// <summary>Processador no limite.</summary>
    Cpu,
    /// <summary>Nenhuma peça claramente no limite.</summary>
    Balanced,
    /// <summary>Sem partida medida: não dá para afirmar.</summary>
    Unknown,
}

/// <summary>
/// Provas para a sugestão de troca de peça e para as dicas por jogo. O
/// veredito (qual peça segura o FPS) sai daqui, calculado do que foi MEDIDO;
/// a IA só escreve a recomendação em cima dele. Sem partida medida, o
/// veredito é "não dá para afirmar", e a IA é instruída a dizer isso.
/// </summary>
public static class UpgradeEvidence
{
    /// <summary>Partidas mais recentes de cada jogo que entram na conta.</summary>
    public const int RecentPerGame = 10;

    public static IReadOnlyList<GameEvidence> Games(IReadOnlyList<GameplaySession> sessions) =>
        sessions
            .GroupBy(s => s.GameId)
            .Select(g =>
            {
                var recent = g.OrderByDescending(s => s.StartedAt).Take(RecentPerGame).ToList();
                var causes = recent.SelectMany(s => StutterExplainer.Explain(s.Timeline, s.Load, s.Stats.AvgFps)).ToList();
                return new GameEvidence(
                    g.Key, recent[0].GameName, recent.Count,
                    Math.Round(recent.Average(s => s.Stats.AvgFps), 1),
                    Math.Round(recent.Average(s => s.Stats.Low1Fps), 1),
                    recent.Select(s => s.DisplayHz).FirstOrDefault(h => h is not null),
                    Avg(recent.Select(s => s.AvgCpuPercent)), Avg(recent.Select(s => s.AvgGpuPercent)),
                    causes.Count,
                    causes.Count(c => c.Kind == DropCauseKind.Gpu),
                    causes.Count(c => c.Kind == DropCauseKind.Cpu),
                    causes.Count(c => c.Kind == DropCauseKind.App),
                    Majority(recent.Select(s => LimitAnalyzer.Diagnose(s).Kind)));
            })
            .OrderByDescending(e => e.Matches)
            .ToList();

    public static Bottleneck Verdict(IReadOnlyList<GameEvidence> games)
    {
        if (games.Count == 0)
            return Bottleneck.Unknown;

        // Partidas com leitura por núcleo têm diagnóstico completo (LimitAnalyzer):
        // ele pesa CPU e GPU juntos e vale mais que a média de uso abaixo.
        var diagnosed = games.Where(g => g.Diagnosed is LimitKind.Cpu or LimitKind.Gpu).ToList();
        if (diagnosed.Count > 0 && diagnosed.All(g => g.Diagnosed == diagnosed[0].Diagnosed))
            return diagnosed[0].Diagnosed == LimitKind.Cpu ? Bottleneck.Cpu : Bottleneck.Gpu;

        // Quedas dizem mais que a média: é nelas que a pessoa sente o jogo travar.
        var drops = games.Sum(g => g.Drops);
        var gpu = games.Sum(g => g.GpuDrops);
        var cpu = games.Sum(g => g.CpuDrops);
        if (drops >= 5)
        {
            if (gpu * 3 >= drops && gpu > cpu)
                return Bottleneck.Gpu;
            if (cpu * 3 >= drops && cpu > gpu)
                return Bottleneck.Cpu;
        }

        var avgGpu = Avg(games.Select(g => g.AvgGpu));
        var avgCpu = Avg(games.Select(g => g.AvgCpu));
        if (avgGpu is >= 95 && avgCpu is null or < 85)
            return Bottleneck.Gpu;
        if (avgCpu is >= 85 && avgGpu is null or < 90)
            return Bottleneck.Cpu;
        return avgGpu is null && avgCpu is null && drops == 0 ? Bottleneck.Unknown : Bottleneck.Balanced;
    }

    /// <summary>Frase curta do veredito para a tela (sai sem IA, e a IA recebe a mesma).</summary>
    public static string VerdictText(Bottleneck b, IReadOnlyList<GameEvidence> games)
    {
        var drops = games.Sum(g => g.Drops);
        return b switch
        {
            Bottleneck.Gpu => drops > 0
                ? $"Nas suas partidas, {games.Sum(g => g.GpuDrops)} de {drops} quedas foram com a placa de vídeo no limite: ela é quem segura o FPS."
                : "Nas suas partidas, a placa de vídeo trabalha no limite: ela é quem segura o FPS.",
            Bottleneck.Cpu => drops > 0
                ? $"Nas suas partidas, {games.Sum(g => g.CpuDrops)} de {drops} quedas foram com o processador no limite: ele é quem segura o FPS."
                : "Nas suas partidas, o processador trabalha no limite: ele é quem segura o FPS.",
            Bottleneck.Balanced => "Nas suas partidas, nenhuma peça ficou claramente no limite.",
            _ => "Ainda não há partida medida. Jogue com o RKZFPS aberto para a sugestão usar o que acontece de verdade no seu PC.",
        };
    }

    /// <summary>Diagnóstico que aparece em mais da metade das partidas conclusivas; null sem maioria.</summary>
    private static LimitKind? Majority(IEnumerable<LimitKind> kinds)
    {
        var list = kinds.Where(k => k != LimitKind.Insufficient).ToList();
        var top = list.GroupBy(k => k).OrderByDescending(g => g.Count()).FirstOrDefault();
        return top is not null && top.Count() * 2 > list.Count ? top.Key : null;
    }

    private static double? Avg(IEnumerable<double?> values)
    {
        var v = values.Where(x => x is not null).Select(x => x!.Value).ToList();
        return v.Count == 0 ? null : Math.Round(v.Average(), 1);
    }
}
