using System.Globalization;

namespace Rkzfps.Core.Benchmark;

public sealed record BenchmarkRun
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string GameId { get; init; } = "";
    public string Process { get; init; } = "";
    public DateTimeOffset At { get; init; }
    public string? SessionId { get; init; }
    public FrameStats Stats { get; init; } = new();
    public double? AvgCpuPercent { get; init; }
    public double? AvgGpuPercent { get; init; }
    public double? AvgRamPercent { get; init; }
    public string SourceCsv { get; init; } = "";
}

public sealed record MetricDelta(string Metric, double Before, double After, double DeltaPercent, bool Significant, bool HigherIsBetter)
{
    public bool Improved => Significant && (HigherIsBetter ? DeltaPercent > 0 : DeltaPercent < 0);

    public bool Worsened => Significant && (HigherIsBetter ? DeltaPercent < 0 : DeltaPercent > 0);
}

public sealed record Comparison(IReadOnlyList<MetricDelta> Metrics, string Verdict, string Method, IReadOnlyList<string> Warnings);

/// <summary>
/// Antes/depois com limiar de ruído. Variação entre duas rodadas iguais do
/// mesmo jogo passa fácil de 3%; sem esse filtro qualquer benchmark
/// "provaria" ganho. Na dúvida, o veredito é "sem diferença significativa".
/// </summary>
public static class BenchmarkComparer
{
    // Limiar com uma rodada de cada lado. Os lows são mais ruidosos que a média.
    private static readonly Dictionary<string, double> SingleRunThreshold = new()
    {
        ["FPS médio"] = 5,
        ["1% low"] = 10,
        ["0.1% low"] = 15,
        ["Frametime médio"] = 5,
    };

    public static Comparison Compare(IReadOnlyList<FrameStats> before, IReadOnlyList<FrameStats> after)
    {
        if (before.Count == 0 || after.Count == 0)
            throw new ArgumentException("É preciso ao menos uma rodada antes e uma depois.");

        var warnings = new List<string>();
        if (before.Concat(after).Any(s => !s.Sufficient))
            warnings.Add("Há rodada com menos de 10 s ou 300 quadros. O resultado não é confiável.");
        var replicated = before.Count >= 2 && after.Count >= 2;
        if (!replicated)
            warnings.Add("Só uma rodada de cada lado: apenas diferenças grandes são consideradas. Rode 3 vezes para medir diferenças menores.");

        var metrics = new List<MetricDelta>
        {
            Delta("FPS médio", before.Select(s => s.AvgFps), after.Select(s => s.AvgFps), true, replicated),
            Delta("1% low", before.Select(s => s.Low1Fps), after.Select(s => s.Low1Fps), true, replicated),
            Delta("0.1% low", before.Select(s => s.Low01Fps), after.Select(s => s.Low01Fps), true, replicated),
            Delta("Frametime médio", before.Select(s => s.AvgFrametimeMs), after.Select(s => s.AvgFrametimeMs), false, replicated),
        };

        var improved = metrics.Where(m => m.Improved).ToList();
        var worsened = metrics.Where(m => m.Worsened).ToList();
        var verdict = (improved.Count, worsened.Count) switch
        {
            (0, 0) => "Não detectamos ganho significativo de desempenho. Sua configuração já estava bem otimizada, ou o efeito das mudanças está dentro da variação normal entre rodadas.",
            (_, 0) => "Ganho significativo em: " + string.Join(", ", improved.Select(Format)) + ".",
            (0, _) => "Piora significativa em: " + string.Join(", ", worsened.Select(Format)) + ". Considere desfazer a última sessão (rkzfps rollback --session).",
            _ => "Resultado misto. Melhorou: " + string.Join(", ", improved.Select(Format)) + ". Piorou: " + string.Join(", ", worsened.Select(Format)) + ".",
        };

        var method = replicated
            ? $"Média de {before.Count} rodada(s) antes e {after.Count} depois; diferença significativa quando maior que 2 erros-padrão e maior que 2%."
            : "Uma rodada de cada lado; limiar fixo de 5% (FPS médio e frametime), 10% (1% low) e 15% (0.1% low).";

        return new Comparison(metrics, verdict, method, warnings);
    }

    private static string Format(MetricDelta m) =>
        $"{m.Metric} {(m.DeltaPercent >= 0 ? "+" : "")}{m.DeltaPercent.ToString("0.0", CultureInfo.InvariantCulture)}%";

    private static MetricDelta Delta(string name, IEnumerable<double> beforeValues, IEnumerable<double> afterValues, bool higherIsBetter, bool replicated)
    {
        var b = beforeValues.ToArray();
        var a = afterValues.ToArray();
        var mb = b.Average();
        var ma = a.Average();
        var pct = mb == 0 ? 0 : 100.0 * (ma - mb) / mb;

        bool significant;
        if (replicated)
        {
            // Welch: erro-padrão da diferença entre as médias.
            var se = Math.Sqrt(Variance(b) / b.Length + Variance(a) / a.Length);
            significant = Math.Abs(ma - mb) > 2 * se && Math.Abs(pct) >= 2;
        }
        else
        {
            significant = Math.Abs(pct) >= SingleRunThreshold[name];
        }

        return new MetricDelta(name, mb, ma, pct, significant, higherIsBetter);
    }

    private static double Variance(double[] values)
    {
        if (values.Length < 2)
            return 0;
        var mean = values.Average();
        return values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1);
    }
}
