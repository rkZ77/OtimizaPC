using System.Text.Json;
using Fpsx.Core.Engine;
using Fpsx.Core.Json;

namespace Fpsx.Core.Benchmark;

// Medição automática em partida real. É o complemento do FPSX Benchmark:
// o benchmark compara um cenário controlado; aqui o app mede cada partida
// sozinho, com o jogo aberto, e mostra se o FPS mudou depois de uma
// otimização. Partida real varia mais (mapa, modo, jogadores), e por isso a
// comparação exige várias partidas e usa o mesmo filtro de ruído.

public sealed record GameplaySession
{
    public string Id { get; init; } = "";
    public string GameId { get; init; } = "";
    public string GameName { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }

    /// <summary>Segundos de jogo em primeiro plano que entraram na conta.</summary>
    public double MeasuredSeconds { get; init; }

    public FrameStats Stats { get; init; } = new();
    public double? AvgCpuPercent { get; init; }
    public double? AvgGpuPercent { get; init; }

    /// <summary>Taxa do monitor na hora: FPS preso nela indica limite (V-Sync, fps_max), não o PC.</summary>
    public int? DisplayHz { get; init; }

    public string AppVersion { get; init; } = "";

    /// <summary>
    /// FPS ao longo da partida para o gráfico: média e pior quadro de cada
    /// trecho. A média sozinha esconde a travada; o pior quadro é o "pico"
    /// para baixo que a pessoa sente no jogo. Vazio em partidas antigas.
    /// </summary>
    public IReadOnlyList<FpsPoint> Timeline { get; init; } = [];
}

/// <param name="T">Segundo da partida (desde o início da medição).</param>
/// <param name="Fps">FPS médio no trecho.</param>
/// <param name="Low">FPS do pior quadro do trecho (1000 / maior frametime).</param>
public sealed record FpsPoint(int T, double Fps, double Low);

public static class GameplayAnalyzer
{
    /// <summary>Carregamento, menu inicial e shaders compilando ficam de fora.</summary>
    public const double WarmupSeconds = 60;

    /// <summary>Menos que isso não diz nada sobre o desempenho da partida.</summary>
    public const double MinMeasuredSeconds = 120;

    /// <summary>Quadro acima disso é pausa (tela de carregamento, alt-tab), não travada de jogo.</summary>
    public const double PauseFrametimeMs = 1000;

    /// <summary>
    /// Frametimes que contam: depois do aquecimento, só nos segundos em que o
    /// jogo estava em primeiro plano. Minimizado ou atrás de outra janela o
    /// jogo desenha diferente, e isso não é desempenho de partida.
    ///
    /// Quando o processo tem mais de uma cadeia de quadros (janela do jogo e
    /// um launcher embutido, por exemplo), vale só a de mais quadros: o
    /// intervalo entre quadros de cadeias diferentes não é frametime de nada.
    /// </summary>
    public static List<double> Filter(IEnumerable<(double TimeSeconds, double FrametimeMs, string SwapChain)> frames, IReadOnlyList<bool> foregroundBySecond) =>
        Analyze(frames, foregroundBySecond).Frametimes;

    /// <summary>Pontos no gráfico: uma partida de 3 horas continua leve de guardar e de desenhar.</summary>
    public const int MaxTimelinePoints = 900;

    /// <summary>Mesmo filtro do <see cref="Filter"/>, mais a linha do tempo do gráfico.</summary>
    public static (List<double> Frametimes, List<FpsPoint> Timeline) Analyze(
        IEnumerable<(double TimeSeconds, double FrametimeMs, string SwapChain)> frames, IReadOnlyList<bool> foregroundBySecond)
    {
        var byChain = new Dictionary<string, List<(double T, double Ms)>>();
        foreach (var (t, ms, chain) in frames)
        {
            if (t < WarmupSeconds || ms <= 0 || ms > PauseFrametimeMs || !double.IsFinite(ms))
                continue;
            var second = (int)t;
            if (second < 0 || second >= foregroundBySecond.Count || !foregroundBySecond[second])
                continue;
            if (!byChain.TryGetValue(chain, out var list))
                byChain[chain] = list = [];
            list.Add((t, ms));
        }

        var main = byChain.Values.OrderByDescending(l => l.Count).FirstOrDefault() ?? [];
        return (main.Select(f => f.Ms).ToList(), Timeline(main));
    }

    private static List<FpsPoint> Timeline(List<(double T, double Ms)> frames)
    {
        if (frames.Count == 0)
            return [];
        var first = (int)frames.Min(f => f.T);
        var span = (int)frames.Max(f => f.T) - first + 1;
        var step = Math.Max(1, (int)Math.Ceiling(span / (double)MaxTimelinePoints));
        return frames
            .GroupBy(f => ((int)f.T - first) / step)
            .OrderBy(g => g.Key)
            .Select(g => new FpsPoint(
                first + g.Key * step,
                Math.Round(g.Count() * 1000 / g.Sum(f => f.Ms), 1),
                Math.Round(1000 / g.Max(f => f.Ms), 1)))
            .ToList();
    }

    /// <summary>
    /// Quedas fortes: trechos seguidos em que o pior quadro caiu abaixo de um
    /// terço da média, ou seja, um quadro 3 vezes mais lento que o normal. É a
    /// travada que se sente, e conta uma vez por queda.
    /// </summary>
    public static int Drops(IReadOnlyList<FpsPoint> timeline, double avgFps)
    {
        var count = 0;
        var inDrop = false;
        foreach (var p in timeline)
        {
            var drop = p.Low < avgFps / 3;
            if (drop && !inDrop)
                count++;
            inDrop = drop;
        }

        return count;
    }

    /// <summary>A sessão, ou null com o motivo quando a partida não serve para medir.</summary>
    public static (GameplaySession? Session, string Reason) Build(
        IReadOnlyList<double> frametimes, string gameId, string gameName, DateTimeOffset startedAt, DateTimeOffset endedAt,
        double? cpu, double? gpu, int? displayHz, string appVersion, IReadOnlyList<FpsPoint>? timeline = null)
    {
        var stats = FrameStats.From(frametimes);
        var measured = frametimes.Sum() / 1000.0;
        if (measured < MinMeasuredSeconds || !stats.Sufficient)
            return (null, $"Partida com {measured / 60:0.#} min medidos em primeiro plano. O mínimo para registrar é {MinMeasuredSeconds / 60:0} min.");

        return (new GameplaySession
        {
            Id = startedAt.ToString("yyyyMMdd-HHmmss") + "-" + gameId,
            GameId = gameId,
            GameName = gameName,
            StartedAt = startedAt,
            EndedAt = endedAt,
            MeasuredSeconds = measured,
            Stats = stats,
            AvgCpuPercent = cpu,
            AvgGpuPercent = gpu,
            DisplayHz = displayHz,
            AppVersion = appVersion,
            Timeline = timeline ?? [],
        }, "");
    }
}

public sealed record GameplayComparison
{
    /// <summary>ready: há partidas dos dois lados. collecting: ainda faltam partidas.</summary>
    public bool Ready { get; init; }

    public string GameName { get; init; } = "";
    public string PivotLabel { get; init; } = "";
    public DateTimeOffset PivotAt { get; init; }
    public int BeforeCount { get; init; }
    public int AfterCount { get; init; }
    public Comparison? Result { get; init; }

    /// <summary>Texto para quem ainda não tem partidas suficientes.</summary>
    public string Status { get; init; } = "";
}

public static class GameplayComparer
{
    /// <summary>Partidas mais próximas da otimização, de cada lado.</summary>
    public const int MaxPerSide = 5;

    public const string RealWorldWarning =
        "Partidas reais variam com mapa, modo e número de jogadores. Para uma comparação controlada, use o FPSX Benchmark no mesmo cenário.";

    public static GameplayComparison Compare(IReadOnlyList<GameplaySession> sessions, string gameId, string gameName, DateTimeOffset pivotAt, string pivotLabel)
    {
        var game = sessions.Where(s => s.GameId == gameId).ToList();
        var before = game.Where(s => s.EndedAt <= pivotAt).OrderByDescending(s => s.StartedAt).Take(MaxPerSide).ToList();
        var after = game.Where(s => s.StartedAt >= pivotAt).OrderBy(s => s.StartedAt).Take(MaxPerSide).ToList();
        var baseResult = new GameplayComparison
        {
            GameName = gameName, PivotLabel = pivotLabel, PivotAt = pivotAt, BeforeCount = before.Count, AfterCount = after.Count,
        };

        if (before.Count == 0 || after.Count == 0)
        {
            var status = (before.Count, after.Count) switch
            {
                (0, 0) => $"Nenhuma partida de {gameName} medida antes nem depois desta otimização.",
                (0, _) => $"Não há partida de {gameName} medida ANTES desta otimização, então não dá para comparar. Da próxima vez, jogue com o FPSX aberto antes de otimizar.",
                _ => $"Jogue {gameName} com o FPSX aberto: depois de 2 partidas o resultado aparece aqui.",
            };
            return baseResult with { Status = status };
        }

        var result = BenchmarkComparer.Compare(before.Select(s => s.Stats).ToList(), after.Select(s => s.Stats).ToList());
        var warnings = result.Warnings.Append(RealWorldWarning).ToList();
        if (before.Concat(after).Any(s => s.DisplayHz is { } hz && s.Stats.AvgFps >= hz * 0.97))
            warnings.Add("O FPS ficou preso perto da taxa do monitor em alguma partida. Com V-Sync ou limite de FPS ligado, otimização não aparece em FPS médio: olhe o 1% low e as travadas.");
        return baseResult with
        {
            Ready = true,
            Result = result with { Warnings = warnings },
            Status = before.Count < 2 || after.Count < 2
                ? "Resultado preliminar: com 2 ou mais partidas de cada lado, diferenças menores passam a ser detectadas."
                : $"Comparando {before.Count} partida(s) antes e {after.Count} depois.",
        };
    }

    /// <summary>Sessões de otimização que servem de marco: aplicadas e não desfeitas.</summary>
    public static IEnumerable<SessionRecord> Pivots(IEnumerable<SessionRecord> sessions) =>
        sessions.Where(s => s.Status == SessionStatus.Completed && s.Changes.Any(c => c.Status == ChangeStatus.Applied))
            .OrderByDescending(s => s.StartedAt);
}

/// <summary>Uma partida por arquivo, em %LOCALAPPDATA%\FPSX\gameplay. Só números, nenhum dado pessoal.</summary>
public sealed class GameplayStore(string dataDirectory)
{
    public string Dir { get; } = Path.Combine(dataDirectory, "gameplay");

    public void Save(GameplaySession session)
    {
        Directory.CreateDirectory(Dir);
        var path = Path.Combine(Dir, session.Id + ".json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(session, FpsxJson.Options));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public IReadOnlyList<GameplaySession> All()
    {
        if (!Directory.Exists(Dir))
            return [];
        var list = new List<GameplaySession>();
        foreach (var file in Directory.EnumerateFiles(Dir, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<GameplaySession>(File.ReadAllText(file), FpsxJson.Options) is { } s)
                    list.Add(s);
            }
            catch (JsonException)
            {
                // Arquivo corrompido não derruba a lista.
            }
        }

        return list.OrderByDescending(s => s.StartedAt).ToList();
    }
}
