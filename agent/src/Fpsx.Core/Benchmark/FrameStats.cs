namespace Fpsx.Core.Benchmark;

/// <summary>
/// Estatísticas de uma captura de frametimes. Definições fixas e documentadas
/// (docs/BENCHMARK.md), para o "1% low" do RKZFPS significar sempre a mesma coisa:
/// 1% low = 1000 / P99 do frametime; 0.1% low = 1000 / P99.9.
/// </summary>
public sealed record FrameStats
{
    public int Frames { get; init; }
    public double DurationSeconds { get; init; }
    public double AvgFps { get; init; }
    public double Low1Fps { get; init; }
    public double Low01Fps { get; init; }
    public double AvgFrametimeMs { get; init; }
    public double MedianFrametimeMs { get; init; }
    public double P99FrametimeMs { get; init; }
    public double MaxFrametimeMs { get; init; }

    /// <summary>Quadros com frametime acima de 2,5x a mediana.</summary>
    public int StutterCount { get; init; }

    public double StuttersPerMinute => DurationSeconds <= 0 ? 0 : StutterCount / (DurationSeconds / 60.0);

    /// <summary>Menos de 10 s ou 300 quadros: o 0.1% low vira ruído puro.</summary>
    public bool Sufficient => Frames >= 300 && DurationSeconds >= 10;

    public const double StutterFactor = 2.5;

    public static FrameStats From(IReadOnlyList<double> frametimesMs)
    {
        var valid = frametimesMs.Where(f => f > 0 && double.IsFinite(f)).ToList();
        if (valid.Count == 0)
            return new FrameStats();

        var sorted = valid.OrderBy(f => f).ToArray();
        var total = valid.Sum();
        var median = Percentile(sorted, 0.5);
        var p99 = Percentile(sorted, 0.99);
        var p999 = Percentile(sorted, 0.999);

        return new FrameStats
        {
            Frames = valid.Count,
            DurationSeconds = total / 1000.0,
            // FPS médio = quadros / tempo, não a média de FPS instantâneos
            // (que supervaloriza os quadros rápidos).
            AvgFps = valid.Count / (total / 1000.0),
            Low1Fps = 1000.0 / p99,
            Low01Fps = 1000.0 / p999,
            AvgFrametimeMs = total / valid.Count,
            MedianFrametimeMs = median,
            P99FrametimeMs = p99,
            MaxFrametimeMs = sorted[^1],
            StutterCount = valid.Count(f => f > median * StutterFactor),
        };
    }

    /// <summary>Nearest-rank: o valor em que pelo menos p dos quadros ficam abaixo ou iguais.</summary>
    public static double Percentile(double[] sorted, double p)
    {
        var rank = (int)Math.Ceiling(p * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }
}
