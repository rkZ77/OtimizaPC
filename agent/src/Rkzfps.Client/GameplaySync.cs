using Rkzfps.Core.Benchmark;

namespace Rkzfps.Client;

/// <summary>
/// O que sobe de uma partida para o servidor, com consentimento: números da
/// partida, o gráfico e o resumo do hardware. Nome de programa, do PC ou do
/// usuário não sobe: das quedas vai só o tipo de causa (programa, CPU, GPU, jogo).
/// </summary>
public sealed record GameplayUpload
{
    public string SessionKey { get; init; } = "";
    public string GameId { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
    public double MeasuredSeconds { get; init; }
    public double AvgFps { get; init; }
    public double Low1Fps { get; init; }
    public double Low01Fps { get; init; }
    public double P99FrametimeMs { get; init; }
    public double StuttersPerMinute { get; init; }
    public double? AvgCpuPercent { get; init; }
    public double? AvgGpuPercent { get; init; }
    public int? DisplayHz { get; init; }
    public string AgentVersion { get; init; } = "";
    public HardwareSummary? Hardware { get; init; }

    /// <summary>[segundo, fps médio, fps do pior quadro], no máximo 900 pontos.</summary>
    public IReadOnlyList<double[]> Timeline { get; init; } = [];

    public int Drops { get; init; }

    /// <summary>Quantas quedas por tipo de causa (app, cpu, gpu, game, unknown).</summary>
    public IReadOnlyDictionary<string, int> DropCauses { get; init; } = new Dictionary<string, int>();

    public static GameplayUpload From(GameplaySession s)
    {
        var causes = StutterExplainer.Explain(s.Timeline, s.Load, s.Stats.AvgFps);
        return new GameplayUpload
        {
            SessionKey = s.Id,
            GameId = s.GameId,
            StartedAt = s.StartedAt,
            EndedAt = s.EndedAt,
            MeasuredSeconds = Math.Round(s.MeasuredSeconds, 1),
            AvgFps = Math.Round(s.Stats.AvgFps, 2),
            Low1Fps = Math.Round(s.Stats.Low1Fps, 2),
            Low01Fps = Math.Round(s.Stats.Low01Fps, 2),
            P99FrametimeMs = Math.Round(s.Stats.P99FrametimeMs, 3),
            StuttersPerMinute = Math.Round(s.Stats.StuttersPerMinute, 2),
            AvgCpuPercent = s.AvgCpuPercent is { } c ? Math.Round(c, 1) : null,
            AvgGpuPercent = s.AvgGpuPercent is { } g ? Math.Round(g, 1) : null,
            DisplayHz = s.DisplayHz,
            AgentVersion = s.AppVersion,
            Hardware = s.Hardware,
            Timeline = s.Timeline.Take(GameplayAnalyzer.MaxTimelinePoints).Select(p => new[] { p.T, p.Fps, p.Low }).ToList(),
            Drops = causes.Count,
            DropCauses = causes.GroupBy(c => c.Kind.ToString().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.Count()),
        };
    }
}
