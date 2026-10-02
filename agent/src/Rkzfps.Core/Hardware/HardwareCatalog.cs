using System.Text.Json;
using System.Text.RegularExpressions;
using Rkzfps.Core.Diagnostics;
using Rkzfps.Core.Json;
using Rkzfps.Core.Model;

namespace Rkzfps.Core.Hardware;

// Modelo de hardware é dado, não código: placa nova entra no hardware.json,
// sem recompilar. O que o RKZFPS tira daqui é o que o Windows não diz
// direito: o que a placa suporta (Reflex, DLSS...) e a classe dela, porque o
// WMI informa no máximo 4 GB de memória de vídeo e isso rebaixava placa forte.

public static class HardwareFeature
{
    public const string Reflex = "reflex";
    public const string Dlss = "dlss";
    public const string FrameGen = "frame_gen";
    public const string AntiLag = "anti_lag";
    public const string Xess = "xess";
    public const string X3d = "x3d";
}

public sealed record HardwareModel
{
    public IReadOnlyList<string> Match { get; init; } = [];
    public string Name { get; init; } = "";
    public GpuVendor Vendor { get; init; }

    /// <summary>LOW, MID ou HIGH, para jogos em 1080p.</summary>
    public HardwareTier Class { get; init; } = HardwareTier.Unknown;

    public IReadOnlyList<string> Features { get; init; } = [];

    public bool Has(string feature) => Features.Contains(feature, StringComparer.OrdinalIgnoreCase);
}

/// <summary>O que foi reconhecido neste PC. Qualquer um pode ser null (modelo fora da lista).</summary>
public sealed record KnownHardware(HardwareModel? Gpu, HardwareModel? Cpu)
{
    public static KnownHardware None { get; } = new(null, null);

    public bool GpuHas(string feature) => Gpu?.Has(feature) == true;

    public bool CpuHas(string feature) => Cpu?.Has(feature) == true;
}

public sealed record HardwareCatalog
{
    public string Version { get; init; } = "";
    public IReadOnlyList<HardwareModel> Gpus { get; init; } = [];
    public IReadOnlyList<HardwareModel> Cpus { get; init; } = [];

    public static HardwareCatalog Empty { get; } = new();

    private static readonly Lazy<HardwareCatalog> _default = new(() => LoadOrEmpty(Path.Combine(AppContext.BaseDirectory, "hardware-db")));

    /// <summary>
    /// O hardware.json que vem ao lado do executável (app, CLI e testes). Sem o
    /// arquivo, nada é reconhecido e tudo funciona como antes.
    /// </summary>
    public static HardwareCatalog Default => _default.Value;

    public static HardwareCatalog LoadOrEmpty(string directory)
    {
        var file = Path.Combine(directory, "hardware.json");
        if (!File.Exists(file))
            return Empty;
        try
        {
            return JsonSerializer.Deserialize<HardwareCatalog>(File.ReadAllText(file), RkzfpsJson.Options) ?? Empty;
        }
        catch (JsonException)
        {
            // Arquivo corrompido não derruba o app: só deixa de reconhecer modelos.
            return Empty;
        }
    }

    /// <summary>Placa dedicada principal e processador deste snapshot.</summary>
    public KnownHardware Recognize(SystemSnapshot s)
    {
        var gpu = s.Gpus.Where(g => !g.LikelyIntegrated).Select(g => FindGpu(g.Name)).FirstOrDefault(m => m is not null);
        return new KnownHardware(gpu, s.Cpu is { } cpu ? FindCpu(cpu.Name) : null);
    }

    public HardwareModel? FindGpu(string? name) => Find(Gpus, name);

    public HardwareModel? FindCpu(string? name) => Find(Cpus, name);

    // Palavra inteira, maior trecho vence: "rx 570" não casa com "RX 5700 XT",
    // e "rtx 5060 ti" vence "rtx 5060" no nome "GeForce RTX 5060 Ti".
    private static HardwareModel? Find(IReadOnlyList<HardwareModel> models, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var text = " " + string.Join(' ', Regex.Replace(name.ToLowerInvariant(), @"\((r|tm)\)|[®™]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries)) + " ";
        HardwareModel? best = null;
        var bestLength = 0;
        foreach (var model in models)
            foreach (var token in model.Match)
            {
                var t = token.Trim().ToLowerInvariant();
                if (t.Length > bestLength && Regex.IsMatch(text, $@"(?<![a-z0-9]){Regex.Escape(t)}(?![a-z0-9])"))
                {
                    best = model;
                    bestLength = t.Length;
                }
            }
        return best;
    }
}
