using System.Globalization;

namespace Rkzfps.Core.Benchmark;

// Diagnóstico de gargalo de uma partida medida. Regra da casa: nenhuma
// categoria sai de uma métrica só. "GPU 99%" sozinho pode ser V-Sync, menu ou
// jogo leve; "CPU 40%" pode esconder um núcleo no limite. Cada veredito exige
// dois sinais que concordam, e a tela mostra os números que sustentam a
// conclusão, separando o que foi medido do que foi calculado e do que não
// deu para ler.

public enum LimitKind
{
    Cpu,
    Gpu,
    Ram,
    Vram,
    Temperature,
    Storage,
    Software,
    Balanced,
    Insufficient,
}

public enum EvidenceKind
{
    /// <summary>Lido direto de um contador do Windows ou do driver.</summary>
    Measured,
    /// <summary>Conta feita em cima de valores medidos (1% low, clock em MHz, proporções).</summary>
    Calculated,
    /// <summary>Não deu para ler: a tela diz por quê, em vez de supor.</summary>
    Unavailable,
}

public sealed record Evidence(EvidenceKind Kind, string Text);

public sealed record LimitDiagnosis
{
    public LimitKind Kind { get; init; }
    public string Title { get; init; } = "";

    /// <summary>Por que o diagnóstico saiu assim, em uma ou duas frases.</summary>
    public string Why { get; init; } = "";

    /// <summary>Os números que sustentam a conclusão.</summary>
    public IReadOnlyList<Evidence> Evidence { get; init; } = [];

    /// <summary>O que resolveria, sem prometer FPS. Vazio quando não há o que indicar.</summary>
    public string Advice { get; init; } = "";
}

/// <summary>Médias e picos da partida, só do que foi lido. Base da tela de histórico e do diagnóstico.</summary>
public sealed record SessionHealth
{
    public int Samples { get; init; }
    public double? CpuAvg { get; init; }
    public double? CpuMaxCoreAvg { get; init; }
    public double? GameThreadAvg { get; init; }
    public double? CpuClockMhzAvg { get; init; }
    public double? CpuClockPercentAvg { get; init; }
    public double? CpuPerfLimitMin { get; init; }
    public double? CpuTempMax { get; init; }
    public double? GpuAvg { get; init; }
    public double? GpuTempAvg { get; init; }
    public double? GpuTempMax { get; init; }
    public double? GpuClockMhzAvg { get; init; }
    public double? GpuClockPercentAvg { get; init; }
    public double? VramAvgMb { get; init; }
    public double? VramMaxMb { get; init; }
    public double? RamAvg { get; init; }
    public double? RamMax { get; init; }
    public double? DiskActiveAvg { get; init; }

    public static SessionHealth From(GameplaySession s) => From(LimitAnalyzer.InGame(s));

    public static SessionHealth From(IReadOnlyList<LoadSample> l) => new()
    {
        Samples = l.Count,
        CpuAvg = Avg(l, x => x.Cpu),
        CpuMaxCoreAvg = Avg(l, x => x.CpuMaxCore),
        GameThreadAvg = Avg(l, x => x.GameThreadMax),
        CpuClockMhzAvg = Avg(l, x => x.CpuClockMhz),
        CpuClockPercentAvg = Avg(l, x => x.CpuClockPercent),
        CpuPerfLimitMin = Min(l, x => x.CpuPerfLimit),
        CpuTempMax = Max(l, x => x.CpuTempC),
        GpuAvg = Avg(l, x => x.Gpu),
        GpuTempAvg = Avg(l, x => x.GpuTempC),
        GpuTempMax = Max(l, x => x.GpuTempC),
        GpuClockMhzAvg = Avg(l, x => x.GpuClockMhz),
        GpuClockPercentAvg = Avg(l, x => x.GpuClockPercent),
        VramAvgMb = Avg(l, x => x.VramUsedMb),
        VramMaxMb = Max(l, x => x.VramUsedMb),
        RamAvg = Avg(l, x => x.RamPercent),
        RamMax = Max(l, x => x.RamPercent),
        DiskActiveAvg = Avg(l, x => x.DiskActivePercent),
    };

    private static IEnumerable<double> Values(IReadOnlyList<LoadSample> l, Func<LoadSample, double?> f) =>
        l.Select(f).Where(v => v is not null && double.IsFinite(v.Value)).Select(v => v!.Value);

    private static double? Avg(IReadOnlyList<LoadSample> l, Func<LoadSample, double?> f) =>
        Values(l, f).DefaultIfEmpty(double.NaN).Average() is var a && double.IsNaN(a) ? null : a;

    private static double? Max(IReadOnlyList<LoadSample> l, Func<LoadSample, double?> f) =>
        Values(l, f).Any() ? Values(l, f).Max() : null;

    private static double? Min(IReadOnlyList<LoadSample> l, Func<LoadSample, double?> f) =>
        Values(l, f).Any() ? Values(l, f).Min() : null;
}

public static class LimitAnalyzer
{
    /// <summary>Amostras (a cada 5 s) depois do aquecimento: menos de 1 minuto não sustenta conclusão.</summary>
    public const int MinSamples = 12;

    /// <summary>Métrica lida em menos da metade das amostras não entra na conta.</summary>
    public const double MinCoverage = 0.5;

    // Limiares. Núcleo a 90% já segura o quadro seguinte: o Windows mede em
    // janelas de 1 s e o jogo alterna a thread principal entre núcleos, então
    // 100% cravado quase nunca aparece mesmo com a CPU limitando.
    public const double CoreBusy = 90;
    public const double GpuBusy = 95;
    public const double GpuWaiting = 85;
    public const double GpuHot = 83;
    public const double CpuHot = 90;
    public const double ClockDropped = 85;
    public const double PerfLimited = 90;
    public const double RamHigh = 90;
    public const double HardFaults = 1000;
    public const double VramFull = 0.95;
    public const double VramSpillMb = 512;
    public const double DiskBusy = 90;

    /// <summary>Fração das amostras em que a condição precisa valer para virar sinal.</summary>
    public const double Sustained = 0.5;

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Amostras da partida de verdade: depois do carregamento e com o jogo na frente.</summary>
    public static IReadOnlyList<LoadSample> InGame(GameplaySession s) =>
        s.Load.Where(l => l.T >= GameplayAnalyzer.WarmupSeconds && l.Foreground != false).ToList();

    public static string Label(LimitKind k) => k switch
    {
        LimitKind.Cpu => "CPU LIMITANDO",
        LimitKind.Gpu => "GPU LIMITANDO",
        LimitKind.Ram => "RAM LIMITANDO",
        LimitKind.Vram => "VRAM LIMITANDO",
        LimitKind.Temperature => "TEMPERATURA LIMITANDO",
        LimitKind.Storage => "ARMAZENAMENTO LIMITANDO",
        LimitKind.Software => "SOFTWARE/CONFIGURAÇÃO",
        LimitKind.Balanced => "SISTEMA EQUILIBRADO",
        _ => "DADOS INSUFICIENTES",
    };

    public static LimitDiagnosis Diagnose(GameplaySession s)
    {
        var l = InGame(s);
        var ev = new List<Evidence>();
        var h = SessionHealth.From(l);
        Describe(s, l, h, ev);

        if (l.Count < MinSamples)
            return Result(LimitKind.Insufficient, ev,
                s.Load.Count == 0
                    ? "Esta partida foi medida antes do diagnóstico existir: só o FPS ficou guardado."
                    : F("Só {0} leituras do PC com o jogo na frente depois do carregamento. São precisas {1} (1 minuto) para afirmar alguma coisa.", l.Count, MinSamples));

        var n = (double)l.Count;
        var coreOf = (LoadSample x) => Busiest(x);
        var coreCov = l.Count(x => coreOf(x) is not null) / n;
        var gpuCov = l.Count(x => x.Gpu is not null) / n;
        var avg = s.Stats.AvgFps;
        var lowRatio = avg > 0 ? s.Stats.Low1Fps / avg : 1;

        // 1. FPS preso num limite: V-Sync, limite do jogo ou do driver. Aí
        // nenhuma peça está limitando, e culpar CPU ou GPU seria mentira.
        var capped = s.DisplayHz is { } hz && avg >= hz * DisplayAdvice.CappedAbove;
        var flat = FlatTimeline(s.Timeline);
        var gpuMed = Median(l, x => x.Gpu);
        var coreMed = Median(l, coreOf);
        if (capped || (flat is not null && gpuMed is < GpuWaiting && coreMed is < CoreBusy))
        {
            var why = capped
                ? F("O FPS médio ({0:0}) ficou colado nos {1} Hz do monitor: V-Sync ou limite de FPS segurou o jogo, não uma peça.", avg, s.DisplayHz)
                : F("O FPS ficou estável perto de {0:0} com a GPU em {1:0}% e o núcleo mais usado em {2:0}%: há um limite de FPS (no jogo, no driver ou V-Sync) segurando o jogo.", flat, gpuMed, coreMed);
            return Result(LimitKind.Software, ev, why + " Se foi você que limitou, está funcionando como deveria. Para ver até onde o PC vai, desligue o limite numa partida de teste.");
        }

        // 2. Jogo no vídeo integrado com a dedicada parada: é configuração, não
        // falta de placa. Vem antes da regra de GPU para nunca virar "GPU LIMITANDO".
        if (s.Gpu is { Status: GpuSelectionStatus.Wrong } wrong)
            return Result(LimitKind.Software, ev,
                wrong.Text + " O desempenho medido nesta partida é o do vídeo integrado, não o da placa dedicada.",
                "Configure o jogo para usar a placa de alto desempenho (botão Ver como corrigir) e compare na próxima partida.");

        // 3. Temperatura: calor alto E clock caindo no mesmo momento.
        var gpuThrottle = Share(l, x => x.GpuTempC is >= GpuHot && x.GpuClockPercent is < ClockDropped && x.Gpu is >= 80, x => x.GpuTempC is not null && x.GpuClockPercent is not null);
        var cpuThrottle = Share(l, x => x.CpuTempC is >= CpuHot && x.CpuPerfLimit is < PerfLimited, x => x.CpuTempC is not null && x.CpuPerfLimit is not null);
        if (gpuThrottle >= 0.25 || cpuThrottle >= 0.25)
        {
            var part = gpuThrottle >= cpuThrottle ? "a placa de vídeo" : "o processador";
            var pct = Math.Max(gpuThrottle, cpuThrottle) * 100;
            return Result(LimitKind.Temperature, ev,
                F("Em {0:0}% da partida {1} estava quente e com o clock reduzido ao mesmo tempo: é o sinal de que ele baixou a velocidade para se proteger do calor.", pct, part),
                "Limpeza de poeira, fluxo de ar no gabinete e, em PC com anos de uso, troca da pasta térmica. Não é caso de trocar peça.");
        }

        // 4. VRAM: dedicada cheia E transbordando para a RAM (ou travadas).
        var vramTotalMb = s.Hardware is { VramGb: > 0, GpuIntegrated: false } hw ? hw.VramGb * 1024 : (double?)null;
        if (vramTotalMb is { } total)
        {
            var full = Share(l, x => x.VramUsedMb >= total * VramFull, x => x.VramUsedMb is not null);
            var sharedMin = l.Select(x => x.SharedGpuMb).Where(v => v is not null).DefaultIfEmpty(null).Min();
            var spill = sharedMin is { } sm ? Share(l, x => x.SharedGpuMb >= sm + VramSpillMb, x => x.SharedGpuMb is not null) : 0;
            if (full >= 0.4 && (spill >= 0.3 || lowRatio < 0.5))
                return Result(LimitKind.Vram, ev,
                    F("A memória de vídeo ficou acima de {0:0}% em {1:0}% da partida e {2}. Quando a VRAM enche, texturas vão para a RAM, que é bem mais lenta, e o jogo engasga.",
                        VramFull * 100, full * 100, spill >= 0.3 ? "a GPU passou a usar memória compartilhada" : F("o 1% low ficou em {0:0}% do FPS médio", lowRatio * 100)),
                    "Baixar qualidade de texturas ou resolução resolve sem gastar. Se continuar, a saída é uma placa com mais memória de vídeo.");
        }

        // 5. RAM: quase cheia E o Windows buscando páginas no disco.
        var ramHigh = Share(l, x => x.RamPercent >= RamHigh, x => x.RamPercent is not null);
        var faults = Share(l, x => x.HardFaultsPerSec >= HardFaults, x => x.HardFaultsPerSec is not null);
        if (ramHigh >= 0.4 && faults >= 0.2)
            return Result(LimitKind.Ram, ev,
                F("A RAM ficou acima de {0:0}% em {1:0}% da partida e, em {2:0}% dela, o Windows teve de ler memória do disco (mais de {3:0} páginas por segundo). Isso trava o jogo enquanto o disco responde.",
                    RamHigh, ramHigh * 100, faults * 100, HardFaults),
                "Feche programas pesados antes de jogar (navegador com muitas abas é o caso mais comum). Se já joga com pouca coisa aberta, mais memória RAM resolve.");

        // 6. Armazenamento: quedas coincidindo com disco no limite, sem CPU nem GPU no limite.
        var drops = StutterExplainer.Explain(s.Timeline, l, avg);
        var diskDrops = drops.Count(d => Near(l, d.T) is { DiskActivePercent: >= DiskBusy } x && coreOf(x) is null or < CoreBusy && x.Gpu is null or < GpuBusy);
        if (drops.Count >= 3 && diskDrops * 2 >= drops.Count)
            return Result(LimitKind.Storage, ev,
                F("{0} de {1} quedas fortes aconteceram com o disco acima de {2:0}% de uso e com processador e placa de vídeo folgados: o jogo esperou o disco carregar dados.", diskDrops, drops.Count, DiskBusy),
                "Instalar o jogo num SSD reduz esse tipo de travada. Se já está num SSD, confira o espaço livre e a saúde do disco na tela inicial.");

        if (coreCov < MinCoverage || gpuCov < MinCoverage)
            return Result(LimitKind.Insufficient, ev, l.All(x => x.Foreground is null)
                ? "Esta partida foi medida antes do diagnóstico completo: sem o uso por núcleo não dá para dizer quem segura o FPS sem chutar. As próximas partidas já trazem essa leitura."
                : "Faltaram leituras de uso por núcleo ou da placa de vídeo nesta partida. Sem as duas não dá para dizer quem segura o FPS sem chutar.");

        // 7. CPU: um núcleo ou a thread do jogo no limite E a GPU esperando.
        var coreBusy = Share(l, x => coreOf(x) >= CoreBusy, x => coreOf(x) is not null);
        var gpuIdle = Share(l, x => x.Gpu < GpuWaiting, x => x.Gpu is not null);
        if (coreBusy >= Sustained && gpuIdle >= Sustained)
            return Result(LimitKind.Cpu, ev,
                F("Em {0:0}% da partida um núcleo (ou a thread principal do jogo) estava acima de {1:0}% enquanto a placa de vídeo ficava abaixo de {2:0}%. A GPU esperava o processador preparar os quadros{3}.",
                    coreBusy * 100, CoreBusy, GpuWaiting, h.CpuAvg is { } c ? F(", mesmo com o uso total do processador em {0:0}%", c) : ""),
                "Nas opções do jogo, baixar o que pesa no processador (distância de visão, multidão, física) e fechar programas em segundo plano. Se continuar, o upgrade que resolve é um processador com mais desempenho por núcleo.");

        // 8. GPU: placa no limite E nenhum núcleo no limite.
        var gpuBusy = Share(l, x => x.Gpu >= GpuBusy, x => x.Gpu is not null);
        var coreFree = Share(l, x => coreOf(x) < CoreBusy, x => coreOf(x) is not null);
        if (gpuBusy >= 0.6 && coreFree >= 0.6)
            return Result(LimitKind.Gpu, ev,
                F("Em {0:0}% da partida a placa de vídeo estava acima de {1:0}% enquanto nenhum núcleo do processador chegava a {2:0}%. É a GPU que define o FPS.",
                    gpuBusy * 100, GpuBusy, CoreBusy),
                "Resolução, sombras, antialiasing e upscaling (DLSS, FSR) são as opções que mais aliviam a placa. Se continuar, o upgrade que resolve é uma placa de vídeo mais forte.");

        // 9. Programa em segundo plano pesando junto com as quedas.
        var appDrops = drops.Count(d => d.Kind == DropCauseKind.App);
        if (drops.Count >= 3 && appDrops * 3 >= drops.Count)
            return Result(LimitKind.Software, ev, StutterExplainer.Summary(drops));

        // 10. Nada no limite e FPS abaixo do monitor: algo além do hardware segura o jogo.
        if (s.DisplayHz is { } dhz && avg < dhz * DisplayAdvice.AlertBelow && gpuMed is < 70 && coreMed is < 70)
            return Result(LimitKind.Software, ev,
                F("O FPS ({0:0}) ficou abaixo dos {1} Hz do monitor com a GPU em {2:0}% e o núcleo mais usado em {3:0}%: nenhuma peça estava no limite. O comum aqui é limite de FPS no jogo, modo de energia econômico ou o próprio motor do jogo.",
                    avg, dhz, gpuMed, coreMed));

        return Result(LimitKind.Balanced, ev,
            "Nenhuma peça ficou no limite de forma sustentada e não houve sinal de calor, falta de memória ou disco segurando o jogo. O desempenho desta partida está bem distribuído.");
    }

    /// <summary>Núcleo mais ocupado ou thread do jogo, o que for maior: os dois apontam CPU presa numa thread.</summary>
    private static double? Busiest(LoadSample x) =>
        (x.CpuMaxCore, x.GameThreadMax) switch
        {
            ({ } a, { } b) => Math.Max(a, b),
            ({ } a, null) => a,
            (null, { } b) => b,
            _ => null,
        };

    /// <summary>
    /// FPS "reto": 80% dos trechos dentro de 3% do mesmo valor. Partida sem
    /// limite oscila bem mais que isso com a cena.
    /// </summary>
    private static double? FlatTimeline(IReadOnlyList<FpsPoint> t)
    {
        if (t.Count < 30)
            return null;
        var sorted = t.Select(p => p.Fps).OrderBy(v => v).ToArray();
        var p10 = sorted[sorted.Length / 10];
        var p90 = sorted[sorted.Length * 9 / 10];
        return p90 > 0 && p10 >= p90 * 0.97 ? FrameStats.Percentile(sorted, 0.5) : null;
    }

    private static LoadSample? Near(IReadOnlyList<LoadSample> l, int t) =>
        l.Where(x => Math.Abs(x.T - t) <= StutterExplainer.MaxGapSeconds).OrderBy(x => Math.Abs(x.T - t)).FirstOrDefault();

    private static double Share(IReadOnlyList<LoadSample> l, Func<LoadSample, bool> cond, Func<LoadSample, bool> has)
    {
        var with = l.Where(has).ToList();
        return with.Count < l.Count * MinCoverage || with.Count == 0 ? 0 : with.Count(cond) / (double)with.Count;
    }

    private static double? Median(IReadOnlyList<LoadSample> l, Func<LoadSample, double?> f)
    {
        var v = l.Select(f).Where(x => x is not null).Select(x => x!.Value).OrderBy(x => x).ToArray();
        return v.Length < l.Count * MinCoverage || v.Length == 0 ? null : FrameStats.Percentile(v, 0.5);
    }

    private static LimitDiagnosis Result(LimitKind kind, List<Evidence> ev, string why, string advice = "") =>
        new() { Kind = kind, Title = Label(kind), Why = why, Evidence = ev, Advice = advice };

    private static string F(string format, params object?[] args) => string.Format(Pt, format, args);

    /// <summary>Tudo que a partida tem (e não tem), com a origem de cada número.</summary>
    private static void Describe(GameplaySession s, IReadOnlyList<LoadSample> l, SessionHealth h, List<Evidence> ev)
    {
        var st = s.Stats;
        ev.Add(new(EvidenceKind.Calculated, F("FPS médio {0:0}, 1% low {1:0}, 0.1% low {2:0}, calculados de {3:N0} quadros medidos pelo registro de quadros do Windows.", st.AvgFps, st.Low1Fps, st.Low01Fps, st.Frames)));
        ev.Add(new(EvidenceKind.Calculated, F("Frametime médio {0:0.0} ms, 99% dos quadros abaixo de {1:0.0} ms, pior quadro {2:0} ms.", st.AvgFrametimeMs, st.P99FrametimeMs, st.MaxFrametimeMs)));
        if (s.DisplayHz is { } hz)
            ev.Add(new(EvidenceKind.Measured, s.ScreenWidth is { } w && s.ScreenHeight is { } hh
                ? F("Tela em {0}x{1} a {2} Hz durante a partida.", w, hh, hz)
                : F("Monitor a {0} Hz.", hz)));

        if (s.Gpu is { } gpu)
            ev.Add(gpu.Status == GpuSelectionStatus.Undetermined
                ? new(EvidenceKind.Unavailable, gpu.Text)
                : new(EvidenceKind.Measured, gpu.Text));

        if (l.Count == 0)
            return;

        // Partida gravada antes das leituras completas (amostra sem o campo de
        // primeiro plano): o que falta não foi "negado pelo driver", só não
        // existia. Dizer o motivo certo evita mandar a pessoa atrás do problema errado.
        if (l.All(x => x.Foreground is null))
        {
            Add(ev, h.CpuAvg, v => F("Processador: {0:0}% de uso total em média.", v), "Uso total do processador não foi lido.");
            Add(ev, h.GpuAvg, v => F("Placa de vídeo: {0:0}% de uso do motor 3D em média.", v), "Uso da placa de vídeo não foi lido.");
            ev.Add(new(EvidenceKind.Unavailable, "Uso por núcleo e por thread, clocks, temperaturas, memória de vídeo, RAM e disco: esta partida foi medida antes de o RKZFPS registrar essas leituras."));
            return;
        }

        Add(ev, h.CpuAvg, v => F("Processador: {0:0}% de uso total em média.", v), "Uso total do processador não foi lido.");
        Add(ev, h.CpuMaxCoreAvg, v => F("Núcleo mais ocupado: {0:0}% em média. É o número que mostra jogo preso numa thread.", v), "Uso por núcleo não foi lido (contador do Windows indisponível).");
        Add(ev, h.GameThreadAvg, v => F("Thread mais ocupada do jogo: {0:0}% de um núcleo em média.", v), "Uso por thread do jogo não disponível: o processo é protegido (anti-cheat) ou o Windows negou a leitura.");
        if (h.CpuClockMhzAvg is { } mhz)
            ev.Add(new(EvidenceKind.Calculated, F("Clock do processador: {0:0} MHz em média ({1:0}% do nominal), calculado pelo Windows.", mhz, h.CpuClockPercentAvg)));
        else
            ev.Add(new(EvidenceKind.Unavailable, "Clock do processador não foi lido."));
        if (h.CpuPerfLimitMin is { } lim && lim < 100)
            ev.Add(new(EvidenceKind.Measured, F("O processador chegou a ser contido em {0:0}% da capacidade (temperatura, energia ou firmware).", lim)));
        Add(ev, h.CpuTempMax, v => F("Zona térmica da placa-mãe: máxima de {0:0} °C. É um sensor da placa, não do núcleo: serve de indício.", v),
            "Temperatura do processador não disponível: o Windows só expõe a zona térmica da placa, e muitas placas não a informam (ou exigem administrador).");
        Add(ev, h.GpuAvg, v => F("Placa de vídeo: {0:0}% de uso do motor 3D em média.", v), "Uso da placa de vídeo não foi lido (contador de GPU do Windows indisponível).");
        Add(ev, h.GpuTempAvg, v => F("Temperatura da placa de vídeo: {0:0} °C em média, máxima {1:0} °C.", v, h.GpuTempMax), "Temperatura da placa de vídeo não disponível: o driver não informa ao Windows.");
        Add(ev, h.GpuClockPercentAvg, v => F("Clock da placa de vídeo: {0:0}% do máximo informado pelo driver, em média.", v), "Clock da placa de vídeo não disponível: o driver não informa ao Windows.");
        var vramGb = s.Hardware?.VramGb;
        Add(ev, h.VramAvgMb, v => vramGb is > 0 && s.Hardware?.GpuIntegrated != true
            ? F("Memória de vídeo: {0:0.0} GB em uso em média, pico de {1:0.0} GB, de {2:0.#} GB.", v / 1024, h.VramMaxMb / 1024, vramGb)
            : F("Memória de vídeo: {0:0.0} GB em uso em média.", v / 1024), "Uso de memória de vídeo não foi lido.");
        Add(ev, h.RamAvg, v => F("RAM: {0:0}% em uso em média, pico de {1:0}%.", v, h.RamMax), "Uso de RAM não foi lido.");
        Add(ev, h.DiskActiveAvg, v => F("Discos: {0:0}% de tempo ativo em média.", v), "Atividade de disco não foi lida.");
    }

    private static void Add(List<Evidence> ev, double? value, Func<double, string> text, string missing) =>
        ev.Add(value is { } v ? new(EvidenceKind.Measured, text(v)) : new(EvidenceKind.Unavailable, missing));
}
