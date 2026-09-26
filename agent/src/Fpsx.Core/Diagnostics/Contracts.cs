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

    /// <summary>Otimização que resolve este problema, quando existe uma. É o botão "Resolver" do app.</summary>
    public string? FixOptimizationId { get; init; }

    /// <summary>Link oficial quando a solução é externa (ex.: driver no site do fabricante).</summary>
    public string? ActionUrl { get; init; }

    /// <summary>
    /// Botões que levam a pessoa a resolver o que o FPSX não resolve sozinho
    /// (driver, backup, espaço). Cada destino é site oficial ou tela do Windows
    /// de uma lista fechada (FindingAction.IsAllowed).
    /// </summary>
    public IReadOnlyList<FindingAction> Actions { get; init; } = [];
}

public sealed record FindingAction(string Label, string Target)
{
    // Telas do Windows que um achado pode abrir. Nada de protocolo livre:
    // um catálogo adulterado não transforma o botão em "abrir qualquer coisa".
    private static readonly HashSet<string> WindowsScreens = new(StringComparer.OrdinalIgnoreCase)
    {
        "ms-settings:windowsupdate",
        "ms-settings:windowsupdate-optionalupdates",
        "ms-settings:backup",
        "ms-settings:storagesense",
        "ms-settings:disksandvolumes",
        "ms-settings:display-advancedgraphics",
    };

    public bool IsAllowed =>
        WindowsScreens.Contains(Target)
        || (Uri.TryCreate(Target, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps);

    public static FindingAction WindowsUpdateDrivers => new("Drivers pelo Windows Update", "ms-settings:windowsupdate-optionalupdates");
}

public interface IDiagnostic
{
    string Id { get; }

    IEnumerable<Finding> Run(EvaluationContext context);
}
