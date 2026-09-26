using System.Text.Json;
using Fpsx.Core.Json;
using Fpsx.Core.Model;

namespace Fpsx.Core.Games;

// Perfil de jogo é dado, não código: um jogo novo entra com um JSON em
// /game-profiles, sem tocar no núcleo. O que exige código é só um tipo novo
// de fonte (steam_userdata, local_appdata, roaming_appdata) ou de formato de
// configuração (valve_kv, ini, colon_kv).

public sealed record GameDetect
{
    public int? SteamAppId { get; init; }
    public IReadOnlyList<string> Processes { get; init; } = [];

    /// <summary>Executável relativo à pasta de instalação (usado na preferência de GPU).</summary>
    public string? Executable { get; init; }

    /// <summary>
    /// Jogo fora da Steam (Epic, launcher próprio): considera instalado quando
    /// o arquivo de configuração existe. Honesto o bastante para o que o
    /// FPSX faz com ele, que é ler e ajustar essa configuração.
    /// </summary>
    public bool ByConfigFile { get; init; }
}

public sealed record GameConfigSource
{
    /// <summary>
    /// steam_userdata: relativo a Steam\userdata\&lt;conta&gt;\.
    /// local_appdata / roaming_appdata: relativo a %LOCALAPPDATA% / %APPDATA%.
    /// </summary>
    public string Source { get; init; } = "";

    public string RelativePath { get; init; } = "";

    /// <summary>valve_kv ("chave" "valor"), ini ("Seção|chave"), colon_kv (chave:valor).</summary>
    public string Format { get; init; } = "";
}

/// <summary>
/// Conjunto de opções gráficas aplicado de uma vez (ex.: PC fraco). Reduzir a
/// qualidade gráfica é o método de maior efeito comprovado em PC limitado
/// pela GPU, e também o que mais muda a imagem: por isso sempre pede confirmação.
/// </summary>
public sealed record GamePreset
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>Chave de configuração e valor aplicado. Chaves fora da whitelist compilada são recusadas.</summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();
}

public sealed record SettingCheck
{
    public string Key { get; init; } = "";
    public IReadOnlyList<string> ExpectAny { get; init; } = [];
    public HealthStatus Severity { get; init; } = HealthStatus.Info;
    public string Title { get; init; } = "";
    public string Why { get; init; } = "";
    public string Recommendation { get; init; } = "";

    /// <summary>Só avalia quando o PC tem GPU deste fabricante (ex.: Reflex só existe em NVIDIA).</summary>
    public GpuVendor? GpuVendor { get; init; }

    /// <summary>Valor que o FPSX grava ao corrigir. Sem ele, o item é só recomendação.</summary>
    public string? FixValue { get; init; }
}

public sealed record RefreshRateCheck
{
    public string NumeratorKey { get; init; } = "";
    public string DenominatorKey { get; init; } = "";
    public string WidthKey { get; init; } = "";
    public string HeightKey { get; init; } = "";
}

public sealed record GameBenchmarkSpec
{
    public string Process { get; init; } = "";
    public int DurationSeconds { get; init; } = 60;
    public int RecommendedRuns { get; init; } = 3;
    public string Method { get; init; } = "";
}

public sealed record GameProfile
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public GameDetect Detect { get; init; } = new();
    public GameConfigSource? Config { get; init; }
    public IReadOnlyList<string> Optimizations { get; init; } = [];
    public IReadOnlyList<SettingCheck> SettingChecks { get; init; } = [];
    public RefreshRateCheck? RefreshRateCheck { get; init; }
    public GameBenchmarkSpec Benchmark { get; init; } = new();
    public IReadOnlyDictionary<string, string> RecommendedSettings { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<GamePreset> Presets { get; init; } = [];

    public static IReadOnlyList<GameProfile> LoadAll(string directory)
    {
        if (!Directory.Exists(directory))
            return [];
        return Directory.EnumerateFiles(directory, "*.json")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => JsonSerializer.Deserialize<GameProfile>(File.ReadAllText(f), FpsxJson.Options)
                         ?? throw new InvalidDataException($"Perfil de jogo vazio: {f}"))
            .ToList();
    }
}
