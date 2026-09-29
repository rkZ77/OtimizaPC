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

    /// <summary>Instagram oficial: novidades de versão e dicas (mesmo link do rodapé do site).</summary>
    public const string InstagramUrl = "https://www.instagram.com/rkzfps.br";

    public bool Allows(Feature f) => PlanFeatures.Allows(License.Plan, f);

    public void RefreshLicense()
    {
        License = Ctx.License();
        CheckRenewal();
    }

    /// <summary>Plano ou teste perto de vencer: título e texto para o aviso da bandeja.</summary>
    public event Action<string, string>? RenewalDue;

    private bool _renewalNotified;

    /// <summary>
    /// Avisa UMA vez por abertura do app (que abre com o Windows, então na
    /// prática uma vez por dia) quando faltam poucos dias. Plano já vencido
    /// não entra aqui: ele aparece no cartão da tela inicial, sem aviso diário.
    /// </summary>
    public void CheckRenewal()
    {
        var now = DateTimeOffset.Now;
        if (_renewalNotified || RenewalDue is null || !License.EndingSoon(now))
            return;
        _renewalNotified = true;
        var days = License.DaysLeft(now) ?? 0;
        var quando = days switch { 0 => "hoje", 1 => "amanhã", _ => $"em {days} dias" };
        var titulo = License.Status == "trial" ? $"Seu teste grátis termina {quando}" : $"Seu plano vence {quando}";
        var recap = Recap().Lines().FirstOrDefault();
        var chamada = License.Status == "trial"
            ? "Windows, driver e jogo mudam com as atualizações: assine para o RKZFPS seguir conferindo e corrigindo de novo."
            : "Renove para o RKZFPS seguir corrigindo e medindo. Os dias novos somam aos que faltam.";
        RenewalDue.Invoke(titulo, (recap is null ? "" : recap + " ") + chamada);
    }

    /// <summary>O que o RKZFPS fez neste PC: correções ativas e ganho medido nas partidas.</summary>
    public Fpsx.Core.Benchmark.PlanRecap Recap() =>
        Fpsx.Core.Benchmark.PlanRecap.Build(Ctx.Store.All(), Ctx.Gameplay.All(), id => Ctx.Catalog.Find(id)?.Name);

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
        // Toda análise vira a base do vigia: o aviso da bandeja compara com a última que a pessoa viu.
        SaveWatchState(ChangeWatch.StateOf(result));
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
            Trial = Ctx.TrialLimit(),
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

    private DateTimeOffset? _lastUpdateCheck;
    private bool _lastUpdateCheckFailed;

    /// <summary>Quando a última verificação terminou com resposta do servidor (a tela Atualizações mostra).</summary>
    public DateTimeOffset? LastUpdateCheck
    {
        get => _lastUpdateCheck;
        private set => Set(ref _lastUpdateCheck, value);
    }

    /// <summary>A última tentativa ficou sem resposta (sem internet ou servidor fora).</summary>
    public bool LastUpdateCheckFailed
    {
        get => _lastUpdateCheckFailed;
        private set => Set(ref _lastUpdateCheckFailed, value);
    }

    public async Task CheckUpdateAsync()
    {
        try
        {
            var found = Updater.Check(await Ctx.Api().SignedUpdateAsync(), AgentContext.Version);
            OnUi(() =>
            {
                Update = found;
                LastUpdateCheck = DateTimeOffset.Now;
                LastUpdateCheckFailed = false;
                if (found is not null && _notifiedVersion != found.Version)
                {
                    _notifiedVersion = found.Version;
                    UpdateFound?.Invoke(found);
                }
            });
        }
        catch (Exception ex) when (ex is ApiException or System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            // Offline: tenta de novo na próxima volta. A tela Atualizações diz
            // que não conseguiu, em vez de afirmar que está tudo em dia.
            OnUi(() => LastUpdateCheckFailed = true);
        }
    }

    /// <summary>Baixa, confere e instala. O instalador fecha e reabre o RKZFPS.</summary>
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
        _monitor.GameSeen += (gameId, exe) => OnUi(() => GameIcons.Remember(gameId, exe));
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

    // ---- vigia do PC e resumo da semana (bandeja) ----
    // Windows Update, driver e patch de jogo mudam configuração sem avisar. O
    // vigia analisa (só leitura) com o app na bandeja e avisa o que mudou;
    // corrigir de novo continua passando pela confirmação de sempre.

    /// <summary>Título e texto para o aviso da bandeja.</summary>
    public event Action<string, string>? WatchNews;

    private System.Threading.Timer? _watchTimer;
    private int _watching;

    private string WatchStatePath => Path.Combine(Ctx.DataDir, "watch-state.json");

    private WatchState? LoadWatchState()
    {
        try
        {
            return File.Exists(WatchStatePath) ? JsonSerializer.Deserialize<WatchState>(File.ReadAllText(WatchStatePath), FpsxJson.Options) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private void SaveWatchState(WatchState state)
    {
        try
        {
            File.WriteAllText(WatchStatePath, JsonSerializer.Serialize(state, FpsxJson.Options));
        }
        catch (IOException)
        {
            // Sem base nova o próximo aviso compara com a anterior: nada se perde.
        }
    }

    /// <summary>Começa a vigiar: primeira volta alguns minutos depois de ligar o PC, depois a cada 6 horas.</summary>
    public void StartWatch(Func<bool> windowVisible) =>
        _watchTimer ??= new System.Threading.Timer(_ => OnUi(() => _ = WatchAsync(windowVisible)), null, TimeSpan.FromMinutes(2), TimeSpan.FromHours(6));

    private async Task WatchAsync(Func<bool> windowVisible)
    {
        // Com a janela aberta a pessoa já vê o Início; e uma análise por vez.
        if (windowVisible() || Interlocked.Exchange(ref _watching, 1) == 1)
            return;
        try
        {
            if (Ctx.Settings.WatchNotify)
            {
                var before = LoadWatchState();
                var scan = await RunScanAsync(new Progress<string>(_ => { }), network: false);
                var report = ChangeWatch.Compare(before, scan, Ctx.Store.All(), id => Ctx.Catalog.Find(id)?.Name);
                if (report.HasNews)
                    WatchNews?.Invoke(report.Title, report.Text);
            }

            WeeklyCheck();
        }
        catch (Exception ex)
        {
            // Vigia é extra: falhar aqui nunca derruba o app na bandeja.
            Dialogs.Log(ex);
        }
        finally
        {
            Interlocked.Exchange(ref _watching, 0);
        }
    }

    private void WeeklyCheck()
    {
        var now = DateTimeOffset.Now;
        var s = Ctx.Settings;
        if (!s.WatchNotify || s.LastWeeklySummaryAt is { } last && now - last < TimeSpan.FromDays(7))
            return;
        if (Fpsx.Core.Benchmark.WeeklySummary.Build(Ctx.Gameplay.All(), now) is not { } summary)
            return;
        Ctx.Storage.SaveSettings(s with { LastWeeklySummaryAt = now });
        WatchNews?.Invoke(summary.Title, summary.Text);
    }

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

    /// <summary>Reabre o RKZFPS elevado (UAC). O Windows pergunta; o usuário pode recusar.</summary>
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
        // Ação do próprio app, antes de tudo: nunca vai para o shell do Windows.
        if (url == Core.Diagnostics.FindingAction.RebootToFirmware)
        {
            RebootToFirmware();
            return;
        }

        // Só http(s) ou uma tela do Windows da lista fechada: link vindo de
        // catálogo/servidor nunca abre executável local.
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        else if (new Core.Diagnostics.FindingAction("", url).IsAllowed)
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>
    /// Reinicia o PC direto na tela da BIOS (UEFI), para a pessoa ligar o
    /// perfil de memória sem precisar acertar a tecla na hora. O RKZFPS não
    /// grava nada na BIOS: só leva até lá. Pede confirmação, porque fecha tudo.
    /// </summary>
    private static void RebootToFirmware()
    {
        if (!Dialogs.Confirm("Reiniciar direto na BIOS",
                "O PC vai reiniciar e abrir a BIOS. Salve e feche o que estiver aberto antes.\n\n" +
                "Na BIOS, siga o passo que aparece no RKZFPS e aperte F10 para salvar. Depois, abra o RKZFPS de novo: ele confere se a memória passou para a velocidade certa.",
                "Reiniciar agora"))
            return;
        try
        {
            // /fw só funciona em PC com UEFI e exige administrador (o Windows pergunta).
            using var p = Process.Start(new ProcessStartInfo("shutdown.exe", "/r /fw /t 5") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            if (p is not null && p.WaitForExit(10000) && p.ExitCode != 0)
                OfereceReinicioNormal(p.ExitCode);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // A pessoa recusou a permissão do Windows: nada acontece.
        }
    }

    /// <summary>
    /// Plano B quando o firmware não aceita o pedido de abrir a BIOS (placa sem
    /// suporte ao "boot to firmware" do UEFI, ou o Windows iniciado em modo
    /// Legacy/CSM). Antes o app só avisava e parava; agora reinicia normalmente
    /// se a pessoa quiser, com a tecla a apertar. O código do erro vai junto
    /// para o suporte saber o que o Windows respondeu.
    /// </summary>
    private static void OfereceReinicioNormal(int codigo)
    {
        if (!Dialogs.Confirm("Não deu para abrir a BIOS direto",
                "A placa-mãe não aceitou o pedido do Windows para abrir a BIOS sozinha " +
                $"(código {codigo}). Dá para entrar do jeito tradicional:\n\n" +
                "1. Toque em Reiniciar normalmente.\n" +
                "2. Assim que a tela acender, aperte Del várias vezes (em notebooks e algumas placas é F2).\n\n" +
                "Salve e feche o que estiver aberto antes.",
                "Reiniciar normalmente"))
            return;
        try
        {
            // Reinício comum não precisa de administrador.
            using var _ = Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
