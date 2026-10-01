using System.Globalization;

namespace Rkzfps.Core.Benchmark;

// Qual placa de vídeo o jogo usou, comparada com a de alto desempenho do PC.
// A GPU do jogo sai do contador de uso do motor 3D POR PROCESSO (o Windows
// separa por adaptador), não do nome da placa nem da ordem em que ela aparece.
// Sem leitura suficiente, o status é "não foi possível determinar".

public enum GpuRole
{
    Integrated,
    Dedicated,
    /// <summary>Adaptador por software, remoto ou de máquina virtual: não roda jogo de verdade.</summary>
    Virtual,
    Unknown,
}

/// <param name="Luid">Chave do adaptador (a mesma do contador de GPU do Windows).</param>
/// <param name="PreferenceOrder">Posição na lista de alto desempenho do Windows (0 = a que o Windows usa para "Alto desempenho"). null = Windows sem essa lista.</param>
public sealed record GpuAdapter(string Luid, string Name, GpuRole Role, long DedicatedMb, int? PreferenceOrder);

public enum GpuSelectionStatus
{
    /// <summary>O jogo usou a placa de alto desempenho.</summary>
    Correct,
    /// <summary>Usou a integrada com uma dedicada disponível: perda provável.</summary>
    Wrong,
    /// <summary>Usou outra dedicada: diferente, mas sem prova de que é pior.</summary>
    OtherDedicated,
    /// <summary>Só uma placa física: nada a escolher.</summary>
    SingleGpu,
    Undetermined,
}

/// <summary>O que fica guardado na partida (nomes e status, sem nada pessoal).</summary>
public sealed record GpuSelection
{
    public GpuSelectionStatus Status { get; init; }
    public string? UsedGpu { get; init; }
    public string? HighPerformanceGpu { get; init; }

    /// <summary>Explicação em uma frase, já com os nomes.</summary>
    public string Text { get; init; } = "";

    /// <summary>Uso médio do motor 3D pelo jogo na placa que ele usou (sustenta o alerta).</summary>
    public double? UsedGpuLoad { get; init; }
}

public static class GpuSelector
{
    /// <summary>Leituras com o jogo usando a GPU (a cada 5 s): menos de 30 s não sustenta conclusão.</summary>
    public const int MinSamples = 6;

    /// <summary>A placa do jogo precisa concentrar isto do uso 3D dele; abaixo, o uso está dividido e não dá para afirmar.</summary>
    public const double Dominance = 0.8;

    /// <summary>Carve-out típico de GPU integrada. Dedicada de verdade passa de 1 GB há mais de dez anos.</summary>
    public const long IntegratedMaxDedicatedMb = 1024;

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Papel da placa pelos sinais do driver, na ordem de confiança: adaptador
    /// virtual ou por software; marcação híbrida do driver (notebook);
    /// memória dedicada; e só por último o nome, como desempate.
    /// </summary>
    public static GpuRole Classify(bool software, bool remoteOrVirtual, bool? hybridIntegrated, bool? hybridDiscrete, long dedicatedMb, bool nameLooksIntegrated)
    {
        if (software || remoteOrVirtual)
            return GpuRole.Virtual;
        if (hybridIntegrated == true)
            return GpuRole.Integrated;
        if (hybridDiscrete == true)
            return GpuRole.Dedicated;
        if (dedicatedMb <= 0)
            return GpuRole.Unknown;
        if (dedicatedMb < IntegratedMaxDedicatedMb)
            return GpuRole.Integrated;
        // APU com memória reservada grande na BIOS: o nome desempata.
        return nameLooksIntegrated ? GpuRole.Integrated : GpuRole.Dedicated;
    }

    /// <summary>
    /// A de alto desempenho: a primeira física da lista de alto desempenho do
    /// Windows, se houver; senão, a dedicada com mais memória.
    /// </summary>
    public static GpuAdapter? HighPerformance(IReadOnlyList<GpuAdapter> adapters)
    {
        var physical = adapters.Where(a => a.Role is GpuRole.Integrated or GpuRole.Dedicated).ToList();
        var byWindows = physical.Where(a => a.PreferenceOrder is not null).OrderBy(a => a.PreferenceOrder).FirstOrDefault();
        var dedicated = physical.Where(a => a.Role == GpuRole.Dedicated).OrderByDescending(a => a.DedicatedMb).FirstOrDefault();
        // O Windows pode listar a integrada primeiro em PC sem dedicada ativa;
        // com uma dedicada presente, ela é a de alto desempenho.
        return byWindows is { Role: GpuRole.Dedicated } ? byWindows : dedicated ?? byWindows;
    }

    /// <param name="gameLoadByLuid">Soma do uso 3D do processo do jogo por adaptador, ao longo da partida.</param>
    /// <param name="samplesWithLoad">Leituras em que o jogo usou alguma GPU.</param>
    public static GpuSelection Evaluate(IReadOnlyList<GpuAdapter> adapters, IReadOnlyDictionary<string, double> gameLoadByLuid, int samplesWithLoad)
    {
        var physical = adapters.Where(a => a.Role is GpuRole.Integrated or GpuRole.Dedicated or GpuRole.Unknown).ToList();
        var best = HighPerformance(adapters);
        if (physical.Count <= 1)
            return new GpuSelection
            {
                Status = GpuSelectionStatus.SingleGpu,
                UsedGpu = physical.FirstOrDefault()?.Name,
                HighPerformanceGpu = best?.Name,
                Text = "Este PC tem uma placa de vídeo física: não há outra para o jogo usar.",
            };

        var total = gameLoadByLuid.Values.Where(v => v > 0).Sum();
        var top = gameLoadByLuid.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).FirstOrDefault();
        var used = top.Key is null ? null : adapters.FirstOrDefault(a => a.Luid == top.Key);
        if (samplesWithLoad < MinSamples || total <= 0 || top.Value < total * Dominance || used is null || best is null || used.Role == GpuRole.Unknown)
        {
            var why = samplesWithLoad < MinSamples || total <= 0
                ? "o Windows não registrou uso de placa de vídeo pelo jogo por tempo suficiente (jogo fechado cedo, protegido ou driver sem o contador)"
                : used is null || used.Role == GpuRole.Unknown
                    ? "o driver não informou o tipo da placa que o jogo usou"
                    : best is null
                        ? "o driver não informou qual placa é a de alto desempenho"
                        : "o uso do jogo ficou dividido entre as placas";
            return new GpuSelection
            {
                Status = GpuSelectionStatus.Undetermined,
                UsedGpu = used?.Name,
                HighPerformanceGpu = best?.Name,
                Text = "Não foi possível determinar a placa de vídeo do jogo: " + why + ".",
            };
        }

        var load = Math.Round(top.Value / samplesWithLoad, 1);
        if (used.Luid == best.Luid)
            return new GpuSelection
            {
                Status = GpuSelectionStatus.Correct, UsedGpu = used.Name, HighPerformanceGpu = best.Name, UsedGpuLoad = load,
                Text = string.Format(Pt, "O jogo usou a {0}, que é a placa de alto desempenho deste PC.", used.Name),
            };

        if (used.Role == GpuRole.Integrated && best.Role == GpuRole.Dedicated)
            return new GpuSelection
            {
                Status = GpuSelectionStatus.Wrong, UsedGpu = used.Name, HighPerformanceGpu = best.Name, UsedGpuLoad = load,
                Text = string.Format(Pt, "O jogo está utilizando a {0} (vídeo integrado, {1:0}% de uso em média) enquanto a {2} (placa dedicada) está disponível.", used.Name, load, best.Name),
            };

        return new GpuSelection
        {
            Status = GpuSelectionStatus.OtherDedicated, UsedGpu = used.Name, HighPerformanceGpu = best.Name, UsedGpuLoad = load,
            Text = string.Format(Pt, "O jogo usou a {0}, e o Windows aponta a {1} como a de alto desempenho. As duas são placas dedicadas: não dá para afirmar que a escolha custou FPS.", used.Name, best.Name),
        };
    }

    /// <summary>Nome com cara de vídeo integrado. Só desempate: nunca decide sozinho quando o driver informa mais.</summary>
    public static bool NameLooksIntegrated(Model.GpuVendor vendor, string name) => vendor switch
    {
        Model.GpuVendor.Intel => !name.Contains("Arc", StringComparison.OrdinalIgnoreCase),
        // APUs aparecem como "AMD Radeon(TM) Graphics" ou "Radeon Vega 8 Graphics", sem modelo RX.
        Model.GpuVendor.Amd => name.EndsWith("Graphics", StringComparison.OrdinalIgnoreCase) && !name.Contains(" RX ", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    public static string Label(GpuSelectionStatus s) => s switch
    {
        GpuSelectionStatus.Correct => "GPU correta",
        GpuSelectionStatus.Wrong => "GPU diferente da GPU de alto desempenho",
        GpuSelectionStatus.OtherDedicated => "Outra placa dedicada",
        GpuSelectionStatus.SingleGpu => "Uma placa de vídeo só",
        _ => "Não foi possível determinar",
    };
}
