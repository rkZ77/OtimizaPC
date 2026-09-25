using System.Text.Json;
using Fpsx.Core.Catalog;
using Fpsx.Core.Engine;
using Fpsx.Core.Games;
using Fpsx.Core.Json;

namespace Fpsx.Agent;

public sealed record LicenseState
{
    public string Plan { get; init; } = "free";
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? LicenseKey { get; init; }
    public string? DeviceId { get; init; }

    /// <summary>Assinatura do servidor. A verificação entra junto com a API de licenças.</summary>
    public string? Signature { get; init; }
}

/// <summary>Caminhos e dependências compartilhados pelos comandos.</summary>
public sealed class AgentContext
{
    public string DataDir { get; }
    public string CatalogDir { get; }
    public string GameProfilesDir { get; }
    public OptimizationCatalog Catalog { get; }
    public IReadOnlyList<GameProfile> GameProfiles { get; }
    public SessionStore Store { get; }

    public AgentContext()
    {
        DataDir = Environment.GetEnvironmentVariable("FPSX_DATA_DIR")
                  ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FPSX");
        Directory.CreateDirectory(DataDir);
        CatalogDir = Path.Combine(AppContext.BaseDirectory, "catalog");
        GameProfilesDir = Path.Combine(AppContext.BaseDirectory, "game-profiles");
        Catalog = OptimizationCatalog.Load(CatalogDir);
        GameProfiles = GameProfile.LoadAll(GameProfilesDir);
        Store = new SessionStore(DataDir);
    }

    public string BenchmarksDir => Path.Combine(DataDir, "benchmarks");

    public string LastScanPath => Path.Combine(DataDir, "last-scan.json");

    /// <summary>
    /// Plano vigente. Sem licença válida o Agent roda como Free: diagnóstico
    /// completo, e só as otimizações do plano Free podem ser aplicadas.
    /// </summary>
    public LicenseState License()
    {
#if DEBUG
        // Só em build de desenvolvimento: permite testar os planos sem a API.
        if (Environment.GetEnvironmentVariable("FPSX_DEV_PLAN") is { Length: > 0 } dev)
            return new LicenseState { Plan = dev };
#endif
        var path = Path.Combine(DataDir, "license.json");
        if (!File.Exists(path))
            return new LicenseState();
        try
        {
            var state = JsonSerializer.Deserialize<LicenseState>(File.ReadAllText(path), FpsxJson.Options) ?? new LicenseState();
            return state.ExpiresAt is { } exp && exp < DateTimeOffset.Now ? new LicenseState() : state;
        }
        catch (JsonException)
        {
            return new LicenseState();
        }
    }
}
