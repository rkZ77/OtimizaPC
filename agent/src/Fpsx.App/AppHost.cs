using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using Fpsx.Client;
using Fpsx.Core.Engine;
using Fpsx.Core.Json;
using Fpsx.Core.Reporting;
using Fpsx.Windows;

namespace Fpsx.App;

/// <summary>
/// Estado compartilhado entre as telas: contexto do Agent, último scan,
/// licença. As telas conversam só por aqui, nunca umas com as outras.
/// </summary>
public sealed class AppHost : ObservableObject
{
    public static AppHost Current { get; } = new();

    public AgentContext Ctx { get; } = new();

    public int WindowsBuild { get; } = Environment.OSVersion.Version.Build;

    public bool IsElevated { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    private ScanResult? _scan;

    public ScanResult? Scan
    {
        get => _scan;
        private set
        {
            if (Set(ref _scan, value))
                Raise(nameof(Report));
        }
    }

    public Report? Report => Scan is null ? null : ReportBuilder.Build(Scan);

    private LicenseState _license = LicenseState.Free();

    public LicenseState License
    {
        get => _license;
        private set
        {
            if (Set(ref _license, value))
                Raise(nameof(PlanLabel));
        }
    }

    public string PlanLabel => License.Plan switch
    {
        "starter" => "Starter",
        "pro" => License.Status == "trial" ? "Pro (teste)" : "Pro",
        "ultimate" => "Ultimate",
        _ => "Free",
    };

    public string SiteUrl => Ctx.Settings.ApiUrl;

    public bool Allows(Feature f) => PlanFeatures.Allows(License.Plan, f);

    public void RefreshLicense() => License = Ctx.License();

    public async Task<ScanResult> RunScanAsync(IProgress<string> progress, bool cpuTest = false, bool network = true)
    {
        var settings = Ctx.Settings;
        var options = new CollectOptions { SampleSeconds = cpuTest ? 15 : 3, CpuStressTest = cpuTest, Network = network };
        var result = await Task.Run(() =>
        {
            var snapshot = new SnapshotCollector(Ctx.GameProfiles).Collect(options, progress.Report);
            return new DecisionEngine(Ctx.Catalog, Ctx.GameProfiles).Evaluate(snapshot, settings.Profile, License.Plan);
        });
        Scan = result;
        Ctx.RecordScan(result);
        File.WriteAllText(Ctx.LastScanPath, JsonSerializer.Serialize(ReportBuilder.Build(result), FpsxJson.Options));
        return result;
    }

    /// <summary>
    /// Aplica com um scan NOVO e rápido: o estado mostrado na tela pode ter
    /// mudado desde o último scan, e aplicar em cima de estado velho é
    /// exatamente o que o motor de decisão existe para evitar.
    /// </summary>
    public async Task<SessionRecord> ApplyAsync(IReadOnlyList<string> ids, bool allowExperimental, IProgress<string> progress)
    {
        var fresh = await Task.Run(() =>
        {
            var snapshot = new SnapshotCollector(Ctx.GameProfiles).Collect(new CollectOptions { SampleSeconds = 1, Network = false }, progress.Report);
            return new DecisionEngine(Ctx.Catalog, Ctx.GameProfiles).Evaluate(snapshot, Ctx.Settings.Profile, License.Plan);
        });

        progress.Report("Criando backup e aplicando");
        var engine = new OptimizationEngine(new WindowsSystemAccess(Ctx.GameProfiles), Ctx.Store);
        var session = await Task.Run(() => engine.Apply(fresh, ids, new ApplyOptions
        {
            AllowExperimental = allowExperimental,
            OnFailure = f => System.Windows.Application.Current.Dispatcher.Invoke(() => Dialogs.Failure(f)),
        }));
        Ctx.RecordSession(session);

        await RunScanAsync(progress, network: false);
        return session;
    }

    /// <summary>Aplica pelo processo elevado. null = o usuário recusou a permissão.</summary>
    public async Task<SessionRecord?> ApplyElevatedAsync(IReadOnlyList<string> ids, bool allowExperimental, IProgress<string> progress)
    {
        progress.Report("Aguardando a permissão do Windows");
        var id = await ElevatedHelper.RunAsync(Ctx, new ElevatedRequest { Action = "apply", Ids = ids, Experimental = allowExperimental });
        if (id is null)
            return null;
        await RunScanAsync(progress, network: false);
        return Ctx.Store.Load(id);
    }

    public async Task<SessionRecord> RollbackAsync(string sessionId, string? changeId, bool force)
    {
        // Desfazer alteração de sistema também precisa da permissão: vai pelo
        // mesmo processo elevado que aplicou.
        var original = Ctx.Store.Load(sessionId);
        var needsAdmin = !IsElevated && original is not null && original.Changes
            .Where(c => changeId is null || c.Id == changeId)
            .Any(c => c.Status == ChangeStatus.Applied && c.Applied.RequiresAdmin);
        if (needsAdmin)
        {
            var id = await ElevatedHelper.RunAsync(Ctx, new ElevatedRequest { Action = "rollback", SessionId = sessionId, ChangeId = changeId, Force = force });
            return (id is null ? null : Ctx.Store.Load(id))
                   ?? throw new InvalidOperationException("Sem a permissão de administrador, as alterações de sistema desta sessão não foram desfeitas.");
        }

        var manager = new RollbackManager(new WindowsSystemAccess(Ctx.GameProfiles), Ctx.Store);
        var result = await Task.Run(() => changeId is null ? manager.RollbackSession(sessionId, force) : manager.RollbackChange(sessionId, changeId, force));
        Ctx.RecordSession(result);
        return result;
    }

    // ---- medição automática das partidas ----

    private GameplayMonitor? _monitor;
    private string _monitorStatus = "Medição automática desligada.";

    /// <summary>Dispara quando uma partida é registrada (já salva no disco).</summary>
    public event Action<Core.Benchmark.GameplaySession>? GameplayRecorded;

    public bool AutoMeasure => Ctx.Settings.AutoMeasure;

    // ---- atualização do app ----

    private SignedRelease? _update;
    private System.Threading.Timer? _updateTimer;
    private string? _notifiedVersion;

    /// <summary>Versão nova, já com assinatura conferida. null = em dia.</summary>
    public SignedRelease? Update
    {
        get => _update;
        private set => Set(ref _update, value);
    }

    /// <summary>Avisa (uma vez por versão) quando surge atualização. Quem mostra é a bandeja.</summary>
    public event Action<SignedRelease>? UpdateFound;

    /// <summary>Confere agora e depois a cada 6 horas: quem deixa o app na bandeja também fica sabendo.</summary>
    public void StartUpdateChecks() =>
        _updateTimer ??= new System.Threading.Timer(_ => _ = CheckUpdateAsync(), null, TimeSpan.FromSeconds(20), TimeSpan.FromHours(6));

    public async Task CheckUpdateAsync()
    {
        try
        {
            var found = Updater.Check(await Ctx.Api().SignedUpdateAsync(), AgentContext.Version);
            OnUi(() =>
            {
                Update = found;
                if (found is not null && _notifiedVersion != found.Version)
                {
                    _notifiedVersion = found.Version;
                    UpdateFound?.Invoke(found);
                }
            });
        }
        catch (ApiException)
        {
            // Offline: tenta de novo na próxima volta.
        }
    }

    /// <summary>Baixa, confere e instala. O instalador fecha e reabre o FPSX.</summary>
    public async Task UpdateNowAsync(IProgress<int> percent)
    {
        if (Update is not { } release)
            return;
        var installer = await Updater.DownloadAsync(release, percent);
        Updater.Install(installer);
        ShutdownMonitor();
        System.Windows.Application.Current.Shutdown();
    }

    // ---- modo simples / avançado e navegação entre telas ----

    public bool AdvancedMode => Ctx.Settings.Mode == "advanced";

    public void SetAdvancedMode(bool advanced)
    {
        if (advanced == AdvancedMode)
            return;
        Ctx.Storage.SaveSettings(Ctx.Settings with { Mode = advanced ? "advanced" : "simple" });
        Raise(nameof(AdvancedMode));
    }

    /// <summary>Uma tela pede para abrir outra (atalhos da tela inicial). Quem escuta é a janela principal.</summary>
    public event Action<Type>? NavigateRequested;

    public void Navigate<T>() => NavigateRequested?.Invoke(typeof(T));

    private string? _liveFps;

    /// <summary>"Counter-Strike 2: 144 FPS agora" enquanto um jogo roda; null fora de partida.</summary>
    public string? LiveFps
    {
        get => _liveFps;
        private set => Set(ref _liveFps, value);
    }

    /// <summary>Últimos minutos da partida em andamento, para o gráfico ao vivo. null fora de partida.</summary>
    public IReadOnlyList<Fpsx.Core.Benchmark.FpsPoint>? LivePoints
    {
        get => _livePoints;
        private set => Set(ref _livePoints, value);
    }

    public string? LiveGame { get; private set; }

    /// <summary>Janela do gráfico ao vivo: 3 minutos mostram a tendência sem virar borrão.</summary>
    public const int LiveWindowSeconds = 180;

    private IReadOnlyList<Fpsx.Core.Benchmark.FpsPoint>? _livePoints;
    private readonly Queue<Fpsx.Core.Benchmark.FpsPoint> _liveBuffer = new();
    private int _liveTick;

    private void OnLive(string game, double? fps, double? low)
    {
        if (fps is not { } f)
        {
            _liveBuffer.Clear();
            _liveTick = 0;
            LiveGame = null;
            LivePoints = null;
            LiveFps = null;
            return;
        }

        LiveGame = game;
        LiveFps = $"{game}: {f:0} FPS agora";
        _liveBuffer.Enqueue(new Fpsx.Core.Benchmark.FpsPoint(_liveTick++, Math.Round(f, 1), Math.Round(low ?? f, 1)));
        while (_liveBuffer.Count > LiveWindowSeconds)
            _liveBuffer.Dequeue();
        // Lista nova a cada segundo: o gráfico redesenha só quando o valor muda.
        LivePoints = _liveBuffer.ToArray();
    }

    public string MonitorStatus
    {
        get => _monitorStatus;
        private set => Set(ref _monitorStatus, value);
    }

    public void SetAutoMeasure(bool on)
    {
        if (on == AutoMeasure)
            return;
        Ctx.Storage.SaveSettings(Ctx.Settings with { AutoMeasure = on });
        if (on)
            StartMonitor();
        else
            StopMonitor();
        Raise(nameof(AutoMeasure));
    }

    public void StartMonitor()
    {
        if (!AutoMeasure || _monitor is { Running: true })
            return;
        _monitor = new GameplayMonitor(Ctx.GameProfiles, Ctx.PresentMonPath, Path.Combine(Ctx.DataDir, "gameplay-tmp"), AgentContext.Version);
        _monitor.StatusChanged += s => OnUi(() => MonitorStatus = s);
        _monitor.LiveFps += (game, fps, low) => OnUi(() => OnLive(game, fps, low));
        _monitor.Recorded += s =>
        {
            // O hardware vai junto: sem ele a partida não se compara com
            // PCs parecidos nem ajuda a calibrar as regras por nível de PC.
            if (Scan is { } scan)
                s = s with { Hardware = Fpsx.Core.Benchmark.HardwareSummary.From(scan.Snapshot) };
            Ctx.Gameplay.Save(s);
            OnUi(() =>
            {
                GameplayRecorded?.Invoke(s);
                // Sobe a partida já (com consentimento). Sem internet, vai na próxima sincronização.
                _ = BackgroundSyncAsync();
            });
        };
        _monitor.Start();
        MonitorStatus = _monitor.Status;
    }

    public void StopMonitor()
    {
        var m = _monitor;
        _monitor = null;
        // Dispose espera a partida em andamento fechar o PresentMon: fora da UI.
        if (m is not null)
            Task.Run(m.Dispose);
        MonitorStatus = "Medição automática desligada.";
    }

    /// <summary>Encerramento do app: espera o monitor salvar o que der da partida em andamento.</summary>
    public void ShutdownMonitor() => _monitor?.Dispose();

    private static void OnUi(Action action) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(action);

    public async Task SyncAsync()
    {
        await Ctx.SyncAsync(WindowsBuild);
        RefreshLicense();
    }

    private async Task BackgroundSyncAsync()
    {
        try
        {
            await SyncAsync();
        }
        catch (Exception ex) when (ex is Fpsx.Client.ApiException or IOException or System.Text.Json.JsonException)
        {
            // Sem rede ou servidor fora: a partida fica na fila e sobe na próxima.
        }
    }

    /// <summary>Reabre o FPSX elevado (UAC). O Windows pergunta; o usuário pode recusar.</summary>
    public static bool RelaunchAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // usuário cancelou o UAC
        }
    }

    public static void OpenUrl(string url)
    {
        // Só http(s) ou uma tela do Windows da lista fechada: link vindo de
        // catálogo/servidor nunca abre executável local.
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        else if (new Core.Diagnostics.FindingAction("", url).IsAllowed)
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
