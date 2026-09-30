using System.Text.Json;
using Fpsx.Core.Engine;
using Fpsx.Core.Json;

namespace Fpsx.Core.Benchmark;

// Medição automática em partida real. É o complemento do RKZFPS Benchmark:
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

    /// <summary>Uso do PC a cada 5 s durante a partida, para explicar as quedas.</summary>
    public IReadOnlyList<LoadSample> Load { get; init; } = [];

    /// <summary>Resumo do hardware na hora da partida (para comparar com PCs parecidos).</summary>
    public HardwareSummary? Hardware { get; init; }

    /// <summary>Resolução da tela principal durante a partida (lida depois do carregamento). null = não lida.</summary>
    public int? ScreenWidth { get; init; }

    public int? ScreenHeight { get; init; }

    /// <summary>
    /// Opções gráficas do arquivo de configuração do jogo, lidas quando a
    /// partida terminou (o jogo grava o que foi usado ao fechar). Só as chaves
    /// que o perfil do jogo conhece. Vazio = jogo sem perfil ou arquivo não lido.
    /// </summary>
    public IReadOnlyDictionary<string, string> GameSettings { get; init; } = new Dictionary<string, string>();

    /// <summary>Jogo reconhecido por tela cheia, sem perfil do RKZFPS.</summary>
    public bool Detected { get; init; }

    /// <summary>Placa de vídeo que o jogo usou e a de alto desempenho do PC. null = partida antiga.</summary>
    public GpuSelection? Gpu { get; init; }
}

/// <param name="T">Segundo da partida, na mesma escala do <see cref="FpsPoint.T"/>.</param>
/// <param name="App">Programa (fora o jogo e o RKZFPS) que mais usou processador no trecho. Fica só no PC.</param>
public sealed record LoadSample(int T, double? Cpu, double? Gpu, string? App, double AppCpu)
{
    // Campos abaixo vieram depois: partida antiga não tem, e null quer dizer
    // "não lido" (nunca zero). O diagnóstico de gargalo trata null como ausente.

    /// <summary>Jogo em primeiro plano no momento da amostra.</summary>
    public bool? Foreground { get; init; }

    /// <summary>Núcleo lógico mais ocupado (% do tempo). Jogo preso numa thread aparece aqui, não no total.</summary>
    public double? CpuMaxCore { get; init; }

    /// <summary>Thread do jogo mais ocupada, em % de um núcleo. null quando o anti-cheat protege o processo.</summary>
    public double? GameThreadMax { get; init; }

    /// <summary>Clock atual do processador em % do nominal (contador "% Processor Performance").</summary>
    public double? CpuClockPercent { get; init; }

    /// <summary>Clock atual calculado pelo Windows (nominal x desempenho), em MHz.</summary>
    public double? CpuClockMhz { get; init; }

    /// <summary>"% Performance Limit": abaixo de 100 o processador está sendo contido (temperatura, energia, firmware).</summary>
    public double? CpuPerfLimit { get; init; }

    /// <summary>Zona térmica ACPI da placa. Não é o sensor do núcleo: serve de indício, não de medida exata.</summary>
    public double? CpuTempC { get; init; }

    public double? GpuTempC { get; init; }
    public double? GpuClockMhz { get; init; }

    /// <summary>Clock do motor 3D em % do máximo informado pelo driver.</summary>
    public double? GpuClockPercent { get; init; }

    /// <summary>Memória de vídeo dedicada em uso no adaptador do jogo.</summary>
    public double? VramUsedMb { get; init; }

    /// <summary>Memória compartilhada usada pela GPU: cresce quando a VRAM transborda para a RAM.</summary>
    public double? SharedGpuMb { get; init; }

    public double? RamPercent { get; init; }

    /// <summary>Páginas lidas do disco por segundo (falta de página que foi ao disco).</summary>
    public double? HardFaultsPerSec { get; init; }

    /// <summary>Tempo ativo dos discos (100 menos o tempo ocioso).</summary>
    public double? DiskActivePercent { get; init; }
}

/// <summary>Por que uma queda aconteceu, na medida do que dá para afirmar.</summary>
public enum DropCauseKind
{
    /// <summary>Outro programa usando processador no mesmo momento.</summary>
    App,
    /// <summary>Processador inteiro no limite.</summary>
    Cpu,
    /// <summary>Placa de vídeo no limite.</summary>
    Gpu,
    /// <summary>Nada fora do normal no PC: o próprio jogo (carregamento, efeito, rede).</summary>
    Game,
    /// <summary>Sem amostra de uso perto da queda.</summary>
    Unknown,
}

public sealed record DropCause(int T, DropCauseKind Kind, string Text, string? App);

/// <summary>
/// Cruza cada queda forte com o uso do PC no mesmo momento. É correlação, não
/// prova: o texto diz "coincidiu com", e quando nada no PC estava fora do
/// normal ele diz isso em vez de culpar alguém.
/// </summary>
public static class StutterExplainer
{
    /// <summary>Programa acima disto no mesmo trecho entra como suspeito.</summary>
    public const double AppCpuPercent = 10;

    public const double CpuLimit = 90;
    public const double GpuLimit = 97;

    /// <summary>Distância máxima entre a queda e a amostra de uso (a amostra é a cada 5 s).</summary>
    public const int MaxGapSeconds = 6;

    public static IReadOnlyList<DropCause> Explain(IReadOnlyList<FpsPoint> timeline, IReadOnlyList<LoadSample> load, double avgFps)
    {
        var causes = new List<DropCause>();
        var inDrop = false;
        foreach (var p in timeline)
        {
            var drop = p.Low < Math.Min(avgFps / 3, GameplayAnalyzer.DropFps);
            if (drop && !inDrop)
                causes.Add(Cause(p.T, load));
            inDrop = drop;
        }

        return causes;
    }

    private static DropCause Cause(int t, IReadOnlyList<LoadSample> load)
    {
        var near = load.Where(l => Math.Abs(l.T - t) <= MaxGapSeconds).OrderBy(l => Math.Abs(l.T - t)).FirstOrDefault();
        var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        if (near is null)
            return new DropCause(t, DropCauseKind.Unknown, "Sem leitura do uso do PC neste momento.", null);
        if (near.App is { } app && near.AppCpu >= AppCpuPercent)
            return new DropCause(t, DropCauseKind.App, string.Format(pt, "Coincidiu com {0} usando {1:0}% do processador.", app, near.AppCpu), app);
        if (near.Cpu >= CpuLimit)
            return new DropCause(t, DropCauseKind.Cpu, string.Format(pt, "Processador no limite ({0:0}%) neste momento.", near.Cpu), null);
        if (near.Gpu >= GpuLimit)
            return new DropCause(t, DropCauseKind.Gpu, string.Format(pt, "Placa de vídeo no limite ({0:0}%) neste momento.", near.Gpu), null);
        return new DropCause(t, DropCauseKind.Game, "Nada fora do normal no PC: provavelmente o próprio jogo (carregamento de área, efeito pesado ou rede).", null);
    }

    /// <summary>Uma frase com o que mais apareceu, para quem não vai passar o mouse no gráfico.</summary>
    public static string Summary(IReadOnlyList<DropCause> causes)
    {
        if (causes.Count == 0)
            return "";
        var apps = causes.Where(c => c.Kind == DropCauseKind.App).GroupBy(c => c.App!).OrderByDescending(g => g.Count()).FirstOrDefault();
        var total = causes.Count;
        if (apps is not null && apps.Count() * 3 >= total)
            return $"{apps.Count()} de {total} quedas coincidiram com o {apps.Key} usando o processador. Feche o {apps.Key} antes de jogar e compare na próxima partida.";
        var cpu = causes.Count(c => c.Kind == DropCauseKind.Cpu);
        if (cpu * 3 >= total)
            return $"{cpu} de {total} quedas foram com o processador no limite. Nesse caso ajuda baixar opções que pesam na CPU (distância de visão, física, jogadores) ou fechar programas.";
        var gpu = causes.Count(c => c.Kind == DropCauseKind.Gpu);
        if (gpu * 3 >= total)
            return $"{gpu} de {total} quedas foram com a placa de vídeo no limite. Baixar resolução, sombras ou antialiasing alivia.";
        var game = causes.Count(c => c.Kind == DropCauseKind.Game);
        return $"{game} de {total} quedas aconteceram com o PC tranquilo: são do próprio jogo (carregamento, efeito ou rede), não de algo rodando junto.";
    }
}

/// <summary>
/// FPS em relação à taxa do monitor. Abaixo da taxa não é defeito, mas o
/// monitor mostra mais quadros do que o PC entrega: o jogo oscila e, sem
/// FreeSync ou G-Sync, a imagem pode rasgar. O alerta diz o que ajuda, sem
/// prometer que o FPS vai alcançar a taxa.
/// </summary>
public static class DisplayAdvice
{
    /// <summary>Abaixo disto (em fração da taxa do monitor) vira alerta.</summary>
    public const double AlertBelow = 0.85;

    /// <summary>Acima disto o FPS está colado na taxa: V-Sync ou limite de FPS.</summary>
    public const double CappedAbove = 0.97;

    public static (bool Alert, string Text)? For(double avgFps, double low1Fps, int? displayHz, IReadOnlyList<FpsPoint>? timeline = null)
    {
        if (displayHz is not { } hz || hz <= 0 || avgFps <= 0)
            return null;
        var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        if (avgFps >= hz * CappedAbove)
            return (false, string.Format(pt, "O FPS acompanhou os {0} Hz do monitor: nesta partida o PC não foi o limite.", hz));
        if (avgFps >= hz * AlertBelow)
            return null;
        // Limite estável: o FPS que o PC segura em 90% dos trechos da partida,
        // arredondado para baixo. O 1% low não serve: são só os piores
        // quadros, e limitar nele jogaria fora metade do FPS (partida real:
        // média 161, 1% low 77). Sem gráfico, não sugere número.
        var cap = timeline is { Count: >= 30 }
            ? (int)(Math.Floor(timeline.Select(p => p.Fps).OrderBy(v => v).ElementAt(timeline.Count / 10) / 10) * 10)
            : 0;
        var capTip = cap >= 30 && cap < hz ? string.Format(pt, " Limitar o FPS no jogo perto de {0} deixa a partida mais estável do que oscilar.", cap) : "";
        return (true, string.Format(pt,
            "Seu monitor é de {0} Hz, mas esta partida ficou em {1:0} FPS de média (1% low {2:0}). Não estraga nada, mas o jogo oscila e, sem FreeSync ou G-Sync, a imagem pode rasgar. " +
            "Se o monitor tiver FreeSync ou G-Sync, ligue no monitor e no painel da placa de vídeo.{3} E use \"Otimizar este jogo\" na tela Jogos.",
            hz, avgFps, low1Fps, capTip));
    }
}

/// <summary>Só o que descreve a máquina para comparar desempenho. Sem nome do PC, usuário ou arquivos.</summary>
public sealed record HardwareSummary
{
    public string Tier { get; init; } = "";
    public string Cpu { get; init; } = "";
    public int Threads { get; init; }
    public string Gpu { get; init; } = "";
    public double VramGb { get; init; }

    /// <summary>GPU integrada: usa a RAM como memória de vídeo, e VRAM "cheia" não quer dizer o mesmo.</summary>
    public bool GpuIntegrated { get; init; }

    public double RamGb { get; init; }
    public int WindowsBuild { get; init; }

    public static HardwareSummary From(Model.SystemSnapshot s)
    {
        const double gb = 1024.0 * 1024 * 1024;
        var gpu = s.Gpus.FirstOrDefault(g => !g.LikelyIntegrated) ?? s.Gpus.FirstOrDefault();
        return new HardwareSummary
        {
            Tier = Diagnostics.HardwareTierClassifier.Assess(s).Tier.ToString().ToUpperInvariant(),
            Cpu = s.Cpu?.Name.Trim() ?? "",
            Threads = s.Cpu?.Threads ?? 0,
            Gpu = gpu?.Name.Trim() ?? "",
            VramGb = Math.Round((gpu?.VramBytes ?? 0) / gb, 1),
            GpuIntegrated = gpu?.LikelyIntegrated ?? false,
            RamGb = Math.Round((s.Memory?.TotalBytes ?? 0) / gb, 1),
            WindowsBuild = s.Os.Build,
        };
    }
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
    /// Quedas fortes: trechos seguidos em que o pior quadro foi 3 vezes mais
    /// lento que a média E passou de 33 ms (abaixo de 30 FPS naquele quadro).
    /// Os dois juntos: a 170 FPS, um quadro de 18 ms é 3 vezes a média e
    /// ninguém sente; na partida real que calibrou isso, só a regra relativa
    /// contava 72 quedas numa partida estável, e as duas juntas contam 11.
    /// </summary>
    /// <summary>Quadro abaixo disto (acima de 33 ms) é travada perceptível em qualquer jogo.</summary>
    public const double DropFps = 30;

    public static int Drops(IReadOnlyList<FpsPoint> timeline, double avgFps)
    {
        var count = 0;
        var inDrop = false;
        foreach (var p in timeline)
        {
            var drop = p.Low < Math.Min(avgFps / 3, DropFps);
            if (drop && !inDrop)
                count++;
            inDrop = drop;
        }

        return count;
    }

    /// <summary>A sessão, ou null com o motivo quando a partida não serve para medir.</summary>
    public static (GameplaySession? Session, string Reason) Build(
        IReadOnlyList<double> frametimes, string gameId, string gameName, DateTimeOffset startedAt, DateTimeOffset endedAt,
        double? cpu, double? gpu, int? displayHz, string appVersion, IReadOnlyList<FpsPoint>? timeline = null, IReadOnlyList<LoadSample>? load = null)
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
            Load = load ?? [],
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

    /// <summary>
    /// Uso médio de CPU e GPU de cada lado. É contexto, não veredito: uso
    /// maior ou menor não quer dizer ganho, e por isso não entra no teste de
    /// diferença significativa. null = partidas sem a leitura.
    /// </summary>
    public (double? Before, double? After) Cpu { get; init; }

    public (double? Before, double? After) Gpu { get; init; }

    /// <summary>Texto para quem ainda não tem partidas suficientes.</summary>
    public string Status { get; init; } = "";
}

public static class GameplayComparer
{
    /// <summary>Partidas mais próximas da otimização, de cada lado.</summary>
    public const int MaxPerSide = 5;

    public const string RealWorldWarning =
        "Partidas reais variam com mapa, modo e número de jogadores. Para uma comparação controlada, use o RKZFPS Benchmark no mesmo cenário.";

    public static GameplayComparison Compare(IReadOnlyList<GameplaySession> sessions, string gameId, string gameName, DateTimeOffset pivotAt, string pivotLabel,
        IReadOnlyList<DateTimeOffset>? shaderClears = null)
    {
        var all = sessions.Where(s => s.GameId == gameId).ToList();
        var tainted = Tainted(all, shaderClears ?? []);
        var game = all.Where(s => !tainted.Contains(s.Id)).ToList();
        var before = game.Where(s => s.EndedAt <= pivotAt).OrderByDescending(s => s.StartedAt).Take(MaxPerSide).ToList();
        var after = game.Where(s => s.StartedAt >= pivotAt).OrderBy(s => s.StartedAt).Take(MaxPerSide).ToList();
        var baseResult = new GameplayComparison
        {
            GameName = gameName, PivotLabel = pivotLabel, PivotAt = pivotAt, BeforeCount = before.Count, AfterCount = after.Count,
        };

        if (before.Count == 0 || after.Count == 0)
        {
            // Havia partida, mas todas desse lado vieram logo depois de limpar o
            // cache de shaders: dizer "não há partida" mandaria jogar à toa.
            var excludedBefore = before.Count == 0 && all.Any(s => s.EndedAt <= pivotAt && tainted.Contains(s.Id));
            var excludedAfter = after.Count == 0 && all.Any(s => s.StartedAt >= pivotAt && tainted.Contains(s.Id));
            if (excludedBefore || excludedAfter)
                return baseResult with
                {
                    Status = $"As partidas de {gameName} medidas {(excludedBefore ? "antes" : "depois")} desta otimização foram jogadas logo depois de limpar o cache de shaders, quando o jogo trava mais por recriar os shaders, e ficaram fora da conta. Jogue mais uma partida {(excludedBefore ? "antes da próxima otimização" : "normalmente")} para comparar.",
                };

            var status = (before.Count, after.Count) switch
            {
                (0, 0) => $"Nenhuma partida de {gameName} medida antes nem depois desta otimização.",
                (0, _) => $"Não há partida de {gameName} medida ANTES desta otimização, então não dá para comparar. Da próxima vez, jogue com o RKZFPS aberto antes de otimizar.",
                _ => $"Jogue {gameName} com o RKZFPS aberto: depois de 2 partidas o resultado aparece aqui.",
            };
            return baseResult with { Status = status };
        }

        var result = BenchmarkComparer.Compare(before.Select(s => s.Stats).ToList(), after.Select(s => s.Stats).ToList());
        var warnings = result.Warnings.Append(RealWorldWarning).ToList();
        if (tainted.Count > 0)
            warnings.Add($"{tainted.Count} partida(s) logo depois de limpar o cache de shaders ficaram fora da conta: o jogo recria os shaders nela e trava mais, o que faria qualquer otimização seguinte parecer melhor do que é.");
        if (before.Concat(after).Any(s => s.DisplayHz is { } hz && s.Stats.AvgFps >= hz * 0.97))
            warnings.Add("O FPS ficou preso perto da taxa do monitor em alguma partida. Com V-Sync ou limite de FPS ligado, otimização não aparece em FPS médio: olhe o 1% low e as travadas.");
        static double? Avg(IEnumerable<double?> v) => v.Where(x => x is not null).Select(x => x!.Value).DefaultIfEmpty(double.NaN).Average() is var a && double.IsNaN(a) ? null : a;
        return baseResult with
        {
            Ready = true,
            Cpu = (Avg(before.Select(s => s.AvgCpuPercent)), Avg(after.Select(s => s.AvgCpuPercent))),
            Gpu = (Avg(before.Select(s => s.AvgGpuPercent)), Avg(after.Select(s => s.AvgGpuPercent))),
            Result = result with { Warnings = warnings },
            Status = before.Count < 2 || after.Count < 2
                ? "Resultado preliminar: com 2 ou mais partidas de cada lado, diferenças menores passam a ser detectadas."
                : $"Comparando {before.Count} partida(s) antes e {after.Count} depois.",
        };
    }

    /// <summary>
    /// Sessões de otimização que servem de marco: aplicadas, não desfeitas e
    /// com alguma mudança que pode mexer no FPS. Limpar cache e reparar rede
    /// não são otimização de desempenho, e comparar em volta delas só mede ruído.
    /// </summary>
    public static IEnumerable<SessionRecord> Pivots(IEnumerable<SessionRecord> sessions) =>
        sessions.Where(s => s.Status == SessionStatus.Completed
                            // Sessão temporária do modo Gaming volta ao fechar o jogo: não é marco.
                            && s.GamingGame is null
                            // Fechar programa e reparar Windows são de uma vez só: não mudam
                            // nada que fique valendo nas partidas seguintes.
                            && s.Changes.Any(c => c.Status == ChangeStatus.Applied
                                                  && c.Applied is not (Model.CacheClearChange or Model.NetworkRepairChange or Model.ProcessCloseChange or Model.SystemRepairChange)))
            .OrderByDescending(s => s.StartedAt);

    private static readonly Model.CacheTarget[] ShaderCaches =
        [Model.CacheTarget.DirectXShaderCache, Model.CacheTarget.NvidiaDxCache, Model.CacheTarget.AmdDxCache, Model.CacheTarget.SteamShaderCacheCs2];

    /// <summary>Quando o cache de shaders foi limpo (a primeira partida depois disso trava mais por natureza).</summary>
    public static IReadOnlyList<DateTimeOffset> ShaderClears(IEnumerable<SessionRecord> sessions) =>
        sessions.SelectMany(s => s.Changes)
            .Where(c => c.Status == ChangeStatus.Applied && c.Applied is Model.CacheClearChange cc && ShaderCaches.Contains(cc.Target))
            .Select(c => c.At)
            .Distinct()
            .ToList();

    /// <summary>A primeira partida de cada jogo depois de cada limpeza de cache de shaders.</summary>
    private static HashSet<string> Tainted(IReadOnlyList<GameplaySession> game, IReadOnlyList<DateTimeOffset> clears) =>
        clears.Select(c => game.Where(s => s.EndedAt > c).OrderBy(s => s.StartedAt).FirstOrDefault()?.Id)
            .OfType<string>()
            .ToHashSet();
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
