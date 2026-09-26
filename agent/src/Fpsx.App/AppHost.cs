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

    public async Task<SessionRecord> RollbackAsync(string sessionId, string? changeId, bool force)
    {
        var manager = new RollbackManager(new WindowsSystemAccess(Ctx.GameProfiles), Ctx.Store);
        var result = await Task.Run(() => changeId is null ? manager.RollbackSession(sessionId, force) : manager.RollbackChange(sessionId, changeId, force));
        Ctx.RecordSession(result);
        return result;
    }

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
        // Só http(s): link vindo de catálogo/servidor nunca abre executável local.
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }
}
