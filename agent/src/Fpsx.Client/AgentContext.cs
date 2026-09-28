using System.Reflection;
using Fpsx.Core.Catalog;
using Fpsx.Core.Engine;
using Fpsx.Core.Games;
using Fpsx.Core.Model;

namespace Fpsx.Client;

public sealed record SyncResult(bool Online, string? Message);

/// <summary>
/// Tudo que o CLI e o app compartilham: caminhos, catálogo (com os overrides
/// assinados do admin), perfis de jogo, sessões, licença e sincronização.
/// </summary>
public sealed class AgentContext
{
    public string DataDir { get; }
    public OptimizationCatalog Catalog { get; private set; }
    public IReadOnlyList<GameProfile> GameProfiles { get; }
    public SessionStore Store { get; }
    public ClientStorage Storage { get; }
    public string DeviceHash { get; } = DeviceIdentity.Hash();

    private readonly OptimizationCatalog _baseCatalog;

    public static string Version =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0";

    public AgentContext(string? dataDir = null)
    {
        DataDir = dataDir ?? Environment.GetEnvironmentVariable("FPSX_DATA_DIR")
                  ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FPSX");
        Directory.CreateDirectory(DataDir);
        _baseCatalog = OptimizationCatalog.Load(Path.Combine(AppContext.BaseDirectory, "catalog"));
        GameProfiles = GameProfile.LoadAll(Path.Combine(AppContext.BaseDirectory, "game-profiles"));
        Store = new SessionStore(DataDir);
        Storage = new ClientStorage(DataDir);
        Catalog = CatalogOverrides.ApplySigned(_baseCatalog, Storage.LoadOverrides());
    }

    public string BenchmarksDir => Path.Combine(DataDir, "benchmarks");

    /// <summary>Partidas medidas automaticamente pelo monitor.</summary>
    public Fpsx.Core.Benchmark.GameplayStore Gameplay => new(DataDir);

    /// <summary>PresentMon que vem no instalador (tools), salvo se o usuário escolheu outro.</summary>
    public string PresentMonPath =>
        Environment.GetEnvironmentVariable("FPSX_PRESENTMON")
        ?? Settings.PresentMonPath
        ?? Path.Combine(AppContext.BaseDirectory, "tools", "PresentMon.exe");

    public string LastScanPath => Path.Combine(DataDir, "last-scan.json");

    public ClientSettings Settings => Storage.LoadSettings();

    /// <summary>
    /// Plano vigente, sempre recalculado do token assinado. Sem licença válida
    /// o FPSX roda como Free: diagnóstico completo e as correções básicas.
    /// </summary>
    public LicenseState License()
    {
#if DEBUG
        // Só em build de desenvolvimento: testar os planos sem a API.
        if (Environment.GetEnvironmentVariable("FPSX_DEV_PLAN") is { Length: > 0 } dev)
            return new LicenseState { Plan = dev, Status = "dev", Email = "dev@local" };
#endif
        return LicenseState.From(Storage.LoadToken(), DeviceHash, DateTimeOffset.UtcNow);
    }

    public DeviceInfo Device(int windowsBuild) =>
        new(DeviceHash, DeviceIdentity.DisplayName(), windowsBuild.ToString(System.Globalization.CultureInfo.InvariantCulture), Version);

    public ApiClient Api() => ApiClient.Create(Settings.ApiUrl);

    /// <summary>Login + ativação deste PC. Em limite de PCs, a ApiException traz a lista para o usuário escolher.</summary>
    public async Task<LicenseState> LoginAsync(string email, string password, int windowsBuild, CancellationToken ct = default)
    {
        var api = Api();
        var access = await api.LoginAsync(email, password, ct);
        var activation = await api.ActivateAsync(access, Device(windowsBuild), ct);
        Storage.SaveToken(activation.Token);
        await SyncAsync(windowsBuild, ct);
        return License();
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (Storage.LoadToken() is { } token)
        {
            try
            {
                // Libera a vaga do PC na conta. Sem internet, o usuário libera pelo site.
                await Api().DeactivateAsync(token, ct);
            }
            catch (ApiException)
            {
            }
        }

        Storage.DeleteToken();
    }

    /// <summary>
    /// Renova licença, baixa overrides do catálogo e envia a telemetria
    /// consentida. Falha de rede não é erro: o app segue com o que tem.
    /// </summary>
    public async Task<SyncResult> SyncAsync(int windowsBuild, CancellationToken ct = default)
    {
        var token = Storage.LoadToken();
        if (token is null)
            return new SyncResult(false, null);

        var api = Api();
        try
        {
            var refreshed = await api.RefreshAsync(token, Device(windowsBuild), ct);
            Storage.SaveToken(refreshed.Token);
            token = refreshed.Token;

            var overrides = await api.CatalogOverridesAsync(token, ct);
            // Só salva o que passou na assinatura: arquivo inválido nunca substitui um válido.
            if (SignedToken.VerifyBody(overrides) is not null)
            {
                Storage.SaveOverrides(overrides);
                Catalog = CatalogOverrides.ApplySigned(_baseCatalog, overrides);
            }

            await FlushTelemetryAsync(api, token, ct);
            await FlushGameplayAsync(api, token, ct);
            return new SyncResult(true, null);
        }
        catch (ApiException ex) when (ex.Status == System.Net.HttpStatusCode.Unauthorized)
        {
            // PC desativado pelo site ou conta bloqueada: a licença local deixa de valer.
            Storage.DeleteToken();
            return new SyncResult(true, ex.Message);
        }
        catch (ApiException ex)
        {
            return new SyncResult(false, ex.Message);
        }
    }

    private async Task FlushTelemetryAsync(ApiClient api, string token, CancellationToken ct)
    {
        if (Settings.TelemetryConsent != true)
        {
            Storage.ClearQueue();
            return;
        }

        var batch = Storage.PeekQueue(100);
        if (batch.Count == 0)
            return;
        await api.SendTelemetryAsync(token, batch, ct);
        Storage.DropFromQueue(batch.Count);
    }

    /// <summary>Partidas por sincronização: o resto vai nas próximas, sem pesar na abertura do app.</summary>
    public const int GameplayBatch = 20;

    /// <summary>
    /// Sobe as partidas ainda não enviadas, só com consentimento. É com elas que
    /// dá para ver, no conjunto de PCs, o que de fato mudou o FPS e calibrar as
    /// regras por nível de hardware.
    /// </summary>
    private async Task FlushGameplayAsync(ApiClient api, string token, CancellationToken ct)
    {
        if (Settings.TelemetryConsent != true)
            return;
        var sent = Storage.UploadedGameplay();
        foreach (var match in Gameplay.All().Where(s => !sent.Contains(s.Id)).OrderBy(s => s.StartedAt).Take(GameplayBatch))
        {
            await api.SendGameplayAsync(token, GameplayUpload.From(match), ct);
            Storage.MarkGameplayUploaded(match.Id);
        }
    }

    /// <summary>
    /// Como PCs parecidos rodam o jogo. Só para quem compartilha as partidas:
    /// a comparação existe porque todos contribuem. null = sem conta, sem
    /// consentimento ou sem internet.
    /// </summary>
    public async Task<PeerStats?> PeersAsync(string gameId, CancellationToken ct = default)
    {
        if (Settings.TelemetryConsent != true || Storage.LoadToken() is not { } token)
            return null;
        try
        {
            var peers = await Api().GameplayPeersAsync(token, gameId, ct);
            return peers.Scope is null ? null : peers;
        }
        catch (ApiException)
        {
            return null;
        }
    }

    /// <summary>Registra o resultado de uma sessão na fila, se o usuário consentiu.</summary>
    public void RecordSession(SessionRecord session)
    {
        if (Settings.TelemetryConsent != true)
            return;
        foreach (var c in session.Changes)
        {
            Storage.Enqueue(new TelemetryEvent
            {
                Event = c.Status == ChangeStatus.Applied ? "optimization_applied" : c.Status == ChangeStatus.RolledBack ? "rollback" : "optimization_failed",
                OptimizationId = c.OptimizationId,
                Success = c.Status is ChangeStatus.Applied or ChangeStatus.RolledBack,
                Detail = c.Error is null
                    ? new() { ["profile"] = session.ProfileId, ["catalog_version"] = session.CatalogVersion }
                    : new() { ["profile"] = session.ProfileId, ["error"] = c.Error },
            });
        }
    }

    public void RecordScan(ScanResult scan)
    {
        if (Settings.TelemetryConsent != true)
            return;
        Storage.Enqueue(new TelemetryEvent
        {
            Event = "scan_completed",
            Success = true,
            Detail = new()
            {
                ["profile"] = scan.ProfileId,
                ["catalog_version"] = scan.CatalogVersion,
                ["plan"] = scan.Plan,
                ["problems"] = scan.Findings.Count(f => f.Status is HealthStatus.Problem or HealthStatus.Attention),
                ["recommended"] = scan.Optimizations.Count(o => o.Decision == Decision.Recommended),
            },
        });
    }
}
