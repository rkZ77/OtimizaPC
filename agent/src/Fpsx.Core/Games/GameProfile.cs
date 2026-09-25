using System.Text.Json;
using Fpsx.Core.Json;
using Fpsx.Core.Model;

namespace Fpsx.Core.Games;

// Perfil de jogo é dado, não código: um jogo novo entra com um JSON em
// /game-profiles, sem tocar no núcleo. O que exige código é só um tipo novo
// de fonte de configuração (hoje: steam_userdata com formato valve_kv).

public sealed record GameDetect
{
    public int? SteamAppId { get; init; }
    public IReadOnlyList<string> Processes { get; init; } = [];
}

public sealed record GameConfigSource
{
    /// <summary>steam_userdata: caminho relativo a Steam\userdata\&lt;conta&gt;\.</summary>
    public string Source { get; init; } = "";

    public string RelativePath { get; init; } = "";

    /// <summary>valve_kv: linhas "chave" "valor".</summary>
    public string Format { get; init; } = "";
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
