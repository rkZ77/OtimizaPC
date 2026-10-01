namespace Rkzfps.Core.Benchmark;

/// <summary>Uma linha do contador "Processor Information" do Windows (uma por núcleo lógico, mais os totais).</summary>
public sealed record CpuCounterRow(string Name, double? Time, double? Utility, double? Performance, double? Limit, double? NominalMhz);

public sealed record CpuReading(double? Total, double? MaxCore, double? ClockPercent, double? ClockMhz, double? PerfLimit);

/// <summary>
/// Contas em cima dos contadores do Windows, separadas da leitura para serem
/// testadas sem Windows. Nenhuma delas completa valor que faltou: sem dado, null.
/// </summary>
public static class CounterMath
{
    /// <summary>
    /// Total, núcleo mais ocupado e clock. O total usa "% Processor Utility"
    /// (o mesmo do Gerenciador de Tarefas); por núcleo vale "% Processor Time",
    /// que mede tempo ocupado e não passa de 100 com turbo.
    /// </summary>
    public static CpuReading Cpu(IReadOnlyList<CpuCounterRow> rows)
    {
        var total = rows.FirstOrDefault(r => r.Name == "_Total");
        // Núcleo: "0,3" (grupo, número). "_Total" e "0,_Total" são somas.
        var cores = rows.Where(r => !r.Name.Contains("_Total", StringComparison.Ordinal) && r.Time is not null).ToList();
        double? maxCore = cores.Count > 0 ? Math.Min(100, cores.Max(r => r.Time!.Value)) : null;
        double? usage = total is null ? null : (total.Utility ?? total.Time) is { } u ? Math.Min(100, u) : null;
        var perf = total?.Performance is > 0 ? total.Performance : null;
        double? mhz = perf is { } p && total?.NominalMhz is > 0 ? Math.Round(total.NominalMhz.Value * p / 100) : null;
        return new CpuReading(usage, maxCore, perf, mhz, total?.Limit);
    }

    /// <summary>"luid_0x00000000_0x0000D1C5_phys_0" vira (low 0xD1C5, high 0). null se o nome não tiver LUID.</summary>
    public static (uint Low, int High)? Luid(string counterName)
    {
        var i = counterName.IndexOf("luid_0x", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return null;
        var parts = counterName[(i + 5)..].Split('_');
        if (parts.Length < 2)
            return null;
        static uint? Hex(string s) =>
            s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && uint.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : null;
        return Hex(parts[0]) is { } high && Hex(parts[1]) is { } low ? (low, unchecked((int)high)) : null;
    }

    /// <summary>Chave do adaptador no nome do contador ("luid_0x..._0x..."), para casar GPU e memória de vídeo.</summary>
    public static string LuidKey(string counterName) =>
        Luid(counterName) is { } l ? $"{l.High:X8}{l.Low:X8}" : "";

    /// <summary>
    /// Thread mais ocupada entre duas leituras, em % de um núcleo. Só conta
    /// thread que existia nas duas; a que nasceu no meio não tem base.
    /// </summary>
    public static double? BusiestThread(IReadOnlyDictionary<int, TimeSpan> before, IReadOnlyDictionary<int, TimeSpan> after, double seconds)
    {
        if (seconds <= 0 || before.Count == 0 || after.Count == 0)
            return null;
        double? max = null;
        foreach (var (id, cpu) in after)
        {
            if (!before.TryGetValue(id, out var prev))
                continue;
            var pct = Math.Clamp((cpu - prev).TotalSeconds / seconds * 100, 0, 100);
            max = max is null ? pct : Math.Max(max.Value, pct);
        }

        return max;
    }

    /// <summary>Temperatura do driver em décimos de grau. Zero ou absurdo é "não informado".</summary>
    public static double? DeciCelsius(uint raw) => raw is > 0 and < 1250 ? raw / 10.0 : null;
}
