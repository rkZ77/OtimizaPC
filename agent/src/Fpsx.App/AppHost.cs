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
        _monitor.Recorded += s =>
        {
            Ctx.Gameplay.Save(s);
            OnUi(() => GameplayRecorded?.Invoke(s));
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
