using System.Reflection;
using Rkzfps.Core.Catalog;
using Rkzfps.Core.Engine;
using Rkzfps.Core.Games;
using Rkzfps.Core.Model;

namespace Rkzfps.Client;

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
        DataDir = dataDir ?? Environment.GetEnvironmentVariable("RKZFPS_DATA_DIR") ?? DefaultDataDir();
        Directory.CreateDirectory(DataDir);
        _baseCatalog = OptimizationCatalog.Load(Path.Combine(AppContext.BaseDirectory, "catalog"));
        GameProfiles = GameProfile.LoadAll(Path.Combine(AppContext.BaseDirectory, "game-profiles"));
        Store = new SessionStore(DataDir);
        Storage = new ClientStorage(DataDir);
        Catalog = CatalogOverrides.ApplySigned(_baseCatalog, Storage.LoadOverrides());
    }

    /// <summary>
    /// %LOCALAPPDATA%\RKZFPS. Até a versão que ainda se chamava FPSX, a pasta
    /// era %LOCALAPPDATA%\FPSX, e é nela que estão os backups do desfazer. Na
    /// primeira abertura a pasta antiga é renomeada inteira: as sessões guardam
    /// caminho relativo, então o desfazer continua valendo. Se a pasta antiga
    /// estiver presa (outro processo com arquivo aberto), o app segue usando
    /// ela e tenta de novo na próxima abertura, em vez de começar sem histórico.
    /// </summary>
    public static string DefaultDataDir(string? localAppData = null)
    {
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(localAppData, "RKZFPS");
        var legacy = Path.Combine(localAppData, "FPSX");
        if (Directory.Exists(dir) || !Directory.Exists(legacy))
            return dir;
        try
        {
            Directory.Move(legacy, dir);
            return dir;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return legacy;
        }
    }

    public string BenchmarksDir => Path.Combine(DataDir, "benchmarks");

    /// <summary>Partidas medidas automaticamente pelo monitor.</summary>
    public Rkzfps.Core.Benchmark.GameplayStore Gameplay => new(DataDir);

    /// <summary>PresentMon que vem no instalador (tools), salvo se o usuário escolheu outro.</summary>
    public string PresentMonPath =>
        Environment.GetEnvironmentVariable("RKZFPS_PRESENTMON")
        ?? Settings.PresentMonPath
        ?? Path.Combine(AppContext.BaseDirectory, "tools", "PresentMon.exe");

    public string LastScanPath => Path.Combine(DataDir, "last-scan.json");

    public ClientSettings Settings => Storage.LoadSettings();

    /// <summary>
    /// Plano vigente, sempre recalculado do token assinado. Sem licença válida
    /// o RKZFPS roda como Free: diagnóstico completo e as correções básicas.
    /// </summary>
    public LicenseState License()
    {
#if DEBUG
        // Só em build de desenvolvimento: testar os planos sem a API.
        if (Environment.GetEnvironmentVariable("RKZFPS_DEV_PLAN") is { Length: > 0 } dev)
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
        if (!Settings.GameplayConsent)
            return;
        var sent = Storage.UploadedGameplay();
        // Jogo reconhecido por tela cheia não sobe: o nome vem do executável e
        // poderia ser qualquer programa da pessoa, não um jogo do catálogo.
        foreach (var match in Gameplay.All().Where(s => !s.Detected && !sent.Contains(s.Id)).OrderBy(s => s.StartedAt).Take(GameplayBatch))
        {
            await api.SendGameplayAsync(token, GameplayUpload.From(match), ct);
            Storage.MarkGameplayUploaded(match.Id);
        }
    }

    /// <summary>
    /// Pede à IA o diagnóstico em palavras simples. Vai só o resumo do
    /// hardware e, de cada item, estado, título, recomendação e efeito
    /// esperado: o detalhe (que pode ter nome de programa) não sai do PC.
    /// </summary>
    public async Task<string> ExplainAsync(IReadOnlyDictionary<string, string> hardware, IEnumerable<Rkzfps.Core.Diagnostics.Finding> findings, CancellationToken ct = default)
    {
        if (Storage.LoadToken() is not { } token)
            throw new ApiException(System.Net.HttpStatusCode.Unauthorized, "Entre na sua conta (tela Conta) para usar a explicação com IA.");
        var items = findings
            .Where(f => f.Status is HealthStatus.Problem or HealthStatus.Attention)
            .Select(f => new
            {
                status = f.Status == HealthStatus.Problem ? "PROBLEMA" : "ATENCAO",
                title = f.Title,
                recommendation = f.Recommendation,
                impact = f.Impact?.ToString(),
            })
            .ToList();
        return await Api().ExplainAsync(token, new { hardware, findings = items }, ct);
    }

    /// <summary>
    /// Sugestão de troca de peça com IA. Vai o resumo do hardware, os títulos
    /// dos achados de montagem (memória abaixo da velocidade, um pente, jogo no
    /// HD), os números das partidas e o veredito calculado aqui. Nada de nome
    /// de programa, arquivo ou pasta.
    /// </summary>
    public async Task<string> UpgradeAsync(IReadOnlyDictionary<string, string> hardware, IEnumerable<string> setup,
        IReadOnlyList<Rkzfps.Core.Benchmark.GameEvidence> games, Rkzfps.Core.Benchmark.Bottleneck verdict, string verdictText, CancellationToken ct = default)
    {
        if (Storage.LoadToken() is not { } token)
            throw new ApiException(System.Net.HttpStatusCode.Unauthorized, "Entre na sua conta (tela Conta) para usar a sugestão com IA.");
        return await Api().UpgradeAsync(token, new
        {
            hardware,
            setup = setup.Take(8).ToList(),
            games = games.Take(6).Select(Evidence).ToList(),
            verdict = verdict.ToString().ToLowerInvariant(),
            verdict_text = verdictText,
        }, ct);
    }

    /// <summary>Dicas de vídeo para um jogo, com o que foi medido nele (quando há).</summary>
    public async Task<string> GameTipsAsync(string game, IReadOnlyDictionary<string, string> hardware, string tier,
        Rkzfps.Core.Benchmark.GameEvidence? measured, CancellationToken ct = default)
    {
        if (Storage.LoadToken() is not { } token)
            throw new ApiException(System.Net.HttpStatusCode.Unauthorized, "Entre na sua conta (tela Conta) para usar as dicas com IA.");
        return await Api().GameTipsAsync(token, new { game, hardware, tier, measured = measured is null ? null : Evidence(measured) }, ct);
    }

    private static object Evidence(Rkzfps.Core.Benchmark.GameEvidence g) => new
    {
        game = g.Game, matches = g.Matches, avg_fps = g.AvgFps, low1_fps = g.Low1Fps, display_hz = g.DisplayHz,
        avg_cpu = g.AvgCpu, avg_gpu = g.AvgGpu, drops = g.Drops, gpu_drops = g.GpuDrops, cpu_drops = g.CpuDrops, app_drops = g.AppDrops,
    };

    /// <summary>
    /// Como PCs parecidos rodam o jogo. Só para quem compartilha as partidas:
    /// a comparação existe porque todos contribuem. null = sem conta, sem
    /// consentimento ou sem internet.
    /// </summary>
    /// <summary>Limite do teste grátis neste PC, ou null fora do teste. Vale para o app, o processo elevado e o CLI.</summary>
    public Rkzfps.Core.Engine.TrialQuota? TrialLimit() =>
        License().Status == "trial" ? Rkzfps.Core.Engine.TrialQuota.From(Store.All()) : null;

    private string? _referralCode;

    /// <summary>
    /// Código de indicação da conta, para o rodapé da imagem compartilhada.
    /// null = sem conta ou sem internet: a imagem sai sem o link, e só.
    /// </summary>
    public async Task<string?> ReferralCodeAsync(CancellationToken ct = default)
    {
        if (_referralCode is not null || Storage.LoadToken() is not { } token)
            return _referralCode;
        try
        {
            return _referralCode = await Api().ReferralCodeAsync(token, ct);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    public async Task<PeerStats?> PeersAsync(string gameId, CancellationToken ct = default)
    {
        if (!Settings.GameplayConsent || Storage.LoadToken() is not { } token)
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
