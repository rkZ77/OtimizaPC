using Fpsx.Core.Model;
using Fpsx.Core.Optimizations;

namespace Fpsx.Core.Diagnostics;

public static class Areas
{
    public const string Windows = "Windows";
    public const string Cpu = "CPU";
    public const string Gpu = "GPU";
    public const string Driver = "Driver";
    public const string Ram = "RAM";
    public const string Storage = "Armazenamento";
    public const string Startup = "Inicialização";
    public const string GameMode = "Game Mode";
    public const string Capture = "Captura";
    public const string Power = "Energia";
    public const string Display = "Monitor";
    public const string Network = "Rede";
    public const string Security = "Segurança";
    public const string Game = "Jogo";
}

/// <summary>Resultado de um diagnóstico: só leitura, nunca altera nada.</summary>
public sealed record Finding
{
    public string DiagnosticId { get; init; } = "";
    public string Area { get; init; } = "";
    public HealthStatus Status { get; init; }
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string? Recommendation { get; init; }

    /// <summary>Onde o efeito aparece: FPS, LATENCY, LOADING, STUTTER, SYSTEM, NETWORK, DISPLAY.</summary>
    public string ImpactArea { get; init; } = "SYSTEM";

    public IReadOnlyDictionary<string, string> Evidence { get; init; } = new Dictionary<string, string>();
}

public interface IDiagnostic
{
    string Id { get; }

    IEnumerable<Finding> Run(EvaluationContext context);
}
