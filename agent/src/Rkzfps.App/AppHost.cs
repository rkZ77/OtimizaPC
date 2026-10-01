using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using Rkzfps.Client;
using Rkzfps.Core.Engine;
using Rkzfps.Core.Json;
using Rkzfps.Core.Reporting;
using Rkzfps.Windows;

namespace Rkzfps.App;

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
        if (License.TrialEnded && !_trialEndHandled)
        {
            _trialEndHandled = true;
            OnUi(() => _ = UndoTrialAsync());
        }
    }

    private bool _trialEndHandled;

    /// <summary>
    /// Teste grátis acabou sem assinatura (confirmado pela licença assinada):
    /// desfaz o que o teste aplicou e o PC volta como estava. Decisão do dono
    /// (01/10/2026), avisada no começo do teste, no site e nos termos.
    ///
    /// Uma vez por abertura do app. Quem escolhe "Assinar para manter" vai ao
    /// pagamento e nada é desfeito agora; se não assinar, a próxima abertura
    /// desfaz. Alteração de sistema pede UMA permissão do Windows para todas
    /// as sessões; recusada, fica para a próxima abertura.
    /// </summary>
    private async Task UndoTrialAsync()
    {
        try
        {
            var pending = TrialEnd.Pending(Ctx.Store.All());
            if (pending.Count == 0)
                return;
            var n = TrialEnd.Count(pending);
            var itens = n == 1 ? "a correção" : $"as {n} correções";
            var escolha = Dialogs.Show("Seu teste grátis terminou",
                $"Como avisado no começo do teste, o RKZFPS vai desfazer {itens} que aplicou nele, e o PC volta como estava. " +
                "O que você mudou depois por conta própria fica como está.\n\n" +
                "Quer manter? Assine um plano: as correções ficam e o RKZFPS segue conferindo e corrigindo a cada abertura.",
                "Desfazer agora", "Assinar para manter");
            if (escolha == 1)
            {
                OpenUrl(Upsell.CheckoutUrl("pro", n));
                return;
            }

            // Alteração do usuário (HKCU, plano de energia, arquivo de jogo) volta
            // aqui mesmo; a de sistema vai junta pelo processo elevado.
            foreach (var s in pending.Where(s => IsElevated || !TrialEnd.NeedsAdmin(s)))
                await RollbackAsync(s.Id, null, force: false);
            List<string> sistema = IsElevated ? [] : pending.Where(TrialEnd.NeedsAdmin).Select(s => s.Id).ToList();
            var sistemaOk = sistema.Count == 0
                || await ElevatedHelper.RunAsync(Ctx, new ElevatedRequest { Action = "rollback-many", Ids = sistema }) is not null;

            await RunScanAsync(new Progress<string>(_ => { }), network: false);
            Raise(nameof(License));
            if (sistemaOk)
                Dialogs.Info("Correções do teste desfeitas", "O PC voltou como estava antes do teste. Quando assinar, é só aplicar de novo pelo RKZFPS.");
            else
                Dialogs.Info("Faltou a permissão do Windows", "As correções do seu usuário foram desfeitas. As de sistema continuam até você permitir: o RKZFPS pede de novo na próxima vez que abrir.");
        }
        catch (Exception ex)
        {
            // Falha aqui nunca derruba o app: o desfazer manual continua no Histórico.
            Dialogs.Log(ex);
        }
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
            ? "No fim do teste, as correções feitas nele são desfeitas e o PC volta como estava. Assine para manter e para o RKZFPS seguir corrigindo."
            : "Renove para o RKZFPS seguir corrigindo e medindo. Os dias novos somam aos que faltam.";
        RenewalDue.Invoke(titulo, (recap is null ? "" : recap + " ") + chamada);
    }

    /// <summary>O que o RKZFPS fez neste PC: correções ativas e ganho medido nas partidas.</summary>
    public Rkzfps.Core.Benchmark.PlanRecap Recap() =>
        Rkzfps.Core.Benchmark.PlanRecap.Build(Ctx.Store.All(), Ctx.Gameplay.All(), id => Ctx.Catalog.Find(id)?.Name);

    public async Task<ScanResult> RunScanAsync(IProgress<string> progress, bool cpuTest = false, bool network = true)
    {
        var settings = Ctx.Settings;
        var options = new CollectOptions { SampleSeconds = cpuTest ? 15 : 3, CpuStressTest = cpuTest, Network = network };
        // Drivers achados na última busca continuam valendo até a próxima busca:
        // uma análise comum (vigia, botão Analisar) não pergunta ao Windows Update.
        var drivers = Scan?.Snapshot.PendingDrivers ?? [];
        var kit = Scan?.Snapshot.DriverKitFolder;
        var result = await Task.Run(() =>
        {
            var snapshot = new SnapshotCollector(Ctx.GameProfiles).Collect(options, progress.Report) with { PendingDrivers = drivers, DriverKitFolder = kit };
            return new DecisionEngine(Ctx.Catalog, Ctx.GameProfiles).Evaluate(snapshot, settings.Profile, License.Plan);
        });
        Scan = result;
        Ctx.RecordScan(result);
        // Toda análise vira a base do vigia: o aviso da bandeja compara com a última que a pessoa viu.
        SaveWatchState(ChangeWatch.StateOf(result));
        CheckHardware(result.Snapshot);
        File.WriteAllText(Ctx.LastScanPath, JsonSerializer.Serialize(ReportBuilder.Build(result), RkzfpsJson.Options));
        return result;
    }

    /// <summary>
    /// Aplica com um scan NOVO e rápido: o estado mostrado na tela pode ter
    /// mudado desde o último scan, e aplicar em cima de estado velho é
    /// exatamente o que o motor de decisão existe para evitar.
    /// </summary>
    /// <summary>Análise rápida (1 s, sem rede) do estado de agora, para aplicar em cima dele.</summary>
    private ScanResult FreshScan(Action<string> progress)
    {
        var last = Scan;
        var snapshot = new SnapshotCollector(Ctx.GameProfiles).Collect(new CollectOptions { SampleSeconds = 1, Network = false }, progress)
            with { PendingDrivers = last?.Snapshot.PendingDrivers ?? [], DriverKitFolder = last?.Snapshot.DriverKitFolder };
        return new DecisionEngine(Ctx.Catalog, Ctx.GameProfiles).Evaluate(snapshot, Ctx.Settings.Profile, License.Plan);
    }

    public async Task<SessionRecord> ApplyAsync(IReadOnlyList<string> ids, bool allowExperimental, IProgress<string> progress)
    {
        var fresh = await Task.Run(() => FreshScan(progress.Report));

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

    /// <summary>
    /// Resultado da busca de drivers no Windows Update: entra no scan atual
    /// para as propostas de instalação existirem (e passarem pelo ApplyFlow).
    /// </summary>
    public void SetPendingDrivers(IReadOnlyList<Rkzfps.Core.Diagnostics.DriverUpdate> drivers)
    {
        if (Scan is not { } scan)
            return;
        Scan = new DecisionEngine(Ctx.Catalog, Ctx.GameProfiles).Evaluate(scan.Snapshot with { PendingDrivers = drivers }, Ctx.Settings.Profile, License.Plan);
    }

    // ---- troca de peça e formatação ----

    private string HardwarePath => Path.Combine(Ctx.DataDir, "hardware.json");
    private string HardwareChangesPath => Path.Combine(Ctx.DataDir, "hardware-changes.json");

    /// <summary>Peças trocadas desde a análise anterior, até a pessoa marcar como visto.</summary>
    public IReadOnlyList<Rkzfps.Core.Diagnostics.HardwareAdvice> HardwareChanges { get; private set; } = [];

    private void CheckHardware(Rkzfps.Core.Model.SystemSnapshot snapshot)
    {
        var now = Rkzfps.Core.Diagnostics.HardwareFingerprint.From(snapshot);
        var before = ReadJson<Rkzfps.Core.Diagnostics.HardwareFingerprint>(HardwarePath);
        var changes = Rkzfps.Core.Diagnostics.HardwareAdvisor.Compare(before, now);
        WriteJson(HardwarePath, now);
        if (changes.Count == 0)
        {
            HardwareChanges = ReadJson<List<Rkzfps.Core.Diagnostics.HardwareAdvice>>(HardwareChangesPath) ?? [];
            return;
        }

        HardwareChanges = changes;
        WriteJson(HardwareChangesPath, changes);
        Raise(nameof(HardwareChanges));
        WatchNews?.Invoke(Rkzfps.Core.Diagnostics.HardwareAdvisor.Title(changes), Rkzfps.Core.Diagnostics.HardwareAdvisor.Text(changes));
    }

    public void DismissHardwareChanges()
    {
        HardwareChanges = [];
        try
        {
            File.Delete(HardwareChangesPath);
        }
        catch (IOException)
        {
        }

        Raise(nameof(HardwareChanges));
    }

    private static T? ReadJson<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), RkzfpsJson.Options) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private static void WriteJson<T>(string path, T value)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(value, RkzfpsJson.Options));
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Salva o kit de drivers na pasta escolhida (processo elevado: o pnputil precisa). null = permissão recusada.</summary>
    public async Task<DriverExportResult?> ExportDriversAsync(string folder, IProgress<string> progress)
    {
        progress.Report("Salvando os drivers deste PC. Pode levar alguns minutos");
        if (IsElevated)
        {
            var snap = Scan?.Snapshot ?? await Task.Run(() => new SnapshotCollector(Ctx.GameProfiles).Collect(new CollectOptions { SampleSeconds = 1, Network = false }));
            return await Task.Run(() => DriverBackup.Export(folder, snap));
        }

        var json = await ElevatedHelper.RunAsync(Ctx, new ElevatedRequest { Action = "export-drivers", Folder = folder });
        return json is null ? null : JsonSerializer.Deserialize<DriverExportResult>(json, RkzfpsJson.Options);
    }

    /// <summary>Kit escolhido para reinstalar: confere antes e, se válido, entra no scan como proposta.</summary>
    public string? SetDriverKit(string folder)
    {
        if (DriverBackup.Check(folder) is { } problem)
            return problem;
        if (Scan is { } scan)
            Scan = new DecisionEngine(Ctx.Catalog, Ctx.GameProfiles).Evaluate(scan.Snapshot with { DriverKitFolder = folder }, Ctx.Settings.Profile, License.Plan);
        return null;
    }

    /// <summary>
    /// Verificação dos arquivos do Windows (só leitura). Precisa de
    /// administrador: vai pelo processo elevado. null = permissão recusada.
    /// </summary>
    public async Task<Rkzfps.Core.Diagnostics.SystemHealthReport?> CheckSystemAsync(IProgress<string> progress)
    {
        progress.Report("Verificando os arquivos do Windows. Pode levar de 5 a 15 minutos");
        if (IsElevated)
            return await Task.Run(WindowsRepair.Check);
        var json = await ElevatedHelper.RunAsync(Ctx, new ElevatedRequest { Action = "system-check" });
        return json is null ? null : JsonSerializer.Deserialize<Rkzfps.Core.Diagnostics.SystemHealthReport>(json, RkzfpsJson.Options);
    }

    /// <summary>Aplica pelo processo elevado. null = o usuário recusou a permissão.</summary>
    public async Task<SessionRecord?> ApplyElevatedAsync(IReadOnlyList<string> ids, bool allowExperimental, IProgress<string> progress)
    {
        progress.Report("Aguardando a permissão do Windows");
        var id = await ElevatedHelper.RunAsync(Ctx, new ElevatedRequest { Action = "apply", Ids = ids, Experimental = allowExperimental, Folder = Scan?.Snapshot.DriverKitFolder });
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
    public IReadOnlyList<Rkzfps.Core.Benchmark.FpsPoint>? LivePoints
    {
        get => _livePoints;
        private set => Set(ref _livePoints, value);
    }

    public string? LiveGame { get; private set; }

    private Rkzfps.Core.Benchmark.LoadSample? _liveHealth;

    /// <summary>Última leitura do PC durante a partida (a cada 5 s). null fora de partida.</summary>
    public Rkzfps.Core.Benchmark.LoadSample? LiveHealth
    {
        get => _liveHealth;
        private set => Set(ref _liveHealth, value);
    }

    /// <summary>Janela do gráfico ao vivo: 3 minutos mostram a tendência sem virar borrão.</summary>
    public const int LiveWindowSeconds = 180;

    private IReadOnlyList<Rkzfps.Core.Benchmark.FpsPoint>? _livePoints;
    private readonly Queue<Rkzfps.Core.Benchmark.FpsPoint> _liveBuffer = new();
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
        _liveBuffer.Enqueue(new Rkzfps.Core.Benchmark.FpsPoint(_liveTick++, Math.Round(f, 1), Math.Round(low ?? f, 1)));
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
        _monitor.LiveHealth += s => OnUi(() => LiveHealth = s);
        // Modo Gaming: roda na linha do monitor, antes da medição começar e
        // depois que ela termina, então início e fim nunca se cruzam.
        _monitor.GameStarted += (_, name) => GamingStart(name);
        _monitor.GameEnded += _ => GamingEnd();
        _monitor.Recorded += s =>
        {
            // O hardware vai junto: sem ele a partida não se compara com
            // PCs parecidos nem ajuda a calibrar as regras por nível de PC.
            if (Scan is { } scan)
                s = s with { Hardware = Rkzfps.Core.Benchmark.HardwareSummary.From(scan.Snapshot) };
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
    public void ShutdownMonitor()
    {
        _monitor?.Dispose();
        // O GameEnded do monitor já restaura; isto cobre a partida que nem
        // chegou a registrar início e fim por erro no meio.
        RestoreGamingLeftovers();
    }

    /// <summary>
    /// Troca de tema: as telas que montam cor em código (selos, diagnóstico)
    /// recarregam pelo mesmo aviso de "análise mudou", sem análise nova.
    /// </summary>
    public void Repaint()
    {
        Raise(nameof(Scan));
        Raise(nameof(GamingMode));
        Raise(nameof(LivePoints));
    }

    // ---- meta de FPS por jogo ----

    public int? FpsGoalOf(string gameId) =>
        Ctx.Settings.FpsGoals.TryGetValue(gameId, out var g) && Rkzfps.Core.Benchmark.FpsGoal.IsValid(g) ? g : null;

    /// <summary>null apaga a meta do jogo.</summary>
    public void SetFpsGoal(string gameId, int? goal)
    {
        var goals = Ctx.Settings.FpsGoals.Where(kv => kv.Key != gameId).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (goal is { } g && Rkzfps.Core.Benchmark.FpsGoal.IsValid(g))
            goals[gameId] = g;
        Ctx.Storage.SaveSettings(Ctx.Settings with { FpsGoals = goals });
    }

    // ---- modo Gaming (Automático / Manual) ----

    private string _gamingStatus = "";

    public GamingModeKind GamingMode => GamingPolicy.Parse(Ctx.Settings.GamingMode);

    public IReadOnlyList<string> GamingAuthorized => Ctx.Settings.GamingAuthorized;

    /// <summary>O que o modo Gaming fez por último (ou por que não fez nada).</summary>
    public string GamingStatus
    {
        get => _gamingStatus;
        private set => Set(ref _gamingStatus, value);
    }

    /// <summary>
    /// Automático depende do monitor de partidas para saber quando o jogo abre
    /// e fecha: ligar o Automático liga a medição automática junto (a tela diz isso).
    /// </summary>
    public void SetGamingMode(GamingModeKind mode)
    {
        if (mode == GamingMode)
            return;
        Ctx.Storage.SaveSettings(Ctx.Settings with { GamingMode = GamingPolicy.Serialize(mode) });
        if (mode == GamingModeKind.Automatic && !AutoMeasure)
            SetAutoMeasure(true);
        // Voltar para o Manual no meio do jogo desfaz o temporário na hora:
        // no Manual nada fica ligado sem a pessoa ter aplicado.
        if (mode == GamingModeKind.Manual)
            Task.Run(() =>
            {
                if (RestoreGamingLeftovers() > 0)
                    OnUi(() => GamingStatus = "Modo Manual: as alterações temporárias do Automático foram desfeitas.");
            });
        Raise(nameof(GamingMode));
    }

    public void SetGamingAuthorized(string optimizationId, bool authorized)
    {
        var list = Ctx.Settings.GamingAuthorized.Where(id => !string.Equals(id, optimizationId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (authorized)
            list.Add(optimizationId);
        Ctx.Storage.SaveSettings(Ctx.Settings with { GamingAuthorized = list });
        Raise(nameof(GamingAuthorized));
    }

    private readonly object _gamingLock = new();

    private void GamingStart(string gameName)
    {
        if (GamingMode != GamingModeKind.Automatic)
            return;
        try
        {
            lock (_gamingLock)
            {
                var outcome = GamingManager().Start(gameName, FreshScan(_ => { }), GamingModeKind.Automatic, GamingAuthorized, Ctx.TrialLimit());
                if (outcome.Session is { } session)
                    Ctx.RecordSession(session);
                OnUi(() => GamingStatus = outcome.Message);
            }
        }
        catch (Exception ex)
        {
            // Erro aqui não pode derrubar a medição nem deixar nada pela metade:
            // o motor já desfez o que entrou, e o registro fica no log.
            Dialogs.Log(ex);
            OnUi(() => GamingStatus = $"{gameName}: o modo Automático não conseguiu aplicar ({ex.Message}). Nada ficou pela metade.");
        }
    }

    private void GamingEnd()
    {
        var restored = RestoreGamingLeftovers();
        if (restored > 0)
            OnUi(() => GamingStatus = "Jogo fechado: as alterações temporárias do modo Automático foram desfeitas.");
    }

    /// <summary>Desfaz sessões temporárias que ainda estejam valendo. Roda ao fechar o jogo, ao abrir e ao fechar o app.</summary>
    public int RestoreGamingLeftovers()
    {
        try
        {
            lock (_gamingLock)
            {
                var restored = GamingManager().Restore();
                foreach (var s in restored)
                    Ctx.RecordSession(s);
                return restored.Count;
            }
        }
        catch (Exception ex)
        {
            Dialogs.Log(ex);
            OnUi(() => GamingStatus = "Não foi possível desfazer as alterações temporárias. Elas continuam no Histórico e podem ser desfeitas por lá.");
            return 0;
        }
    }

    private GamingSessionManager GamingManager() => new(new WindowsSystemAccess(Ctx.GameProfiles), Ctx.Store);

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
            return File.Exists(WatchStatePath) ? JsonSerializer.Deserialize<WatchState>(File.ReadAllText(WatchStatePath), RkzfpsJson.Options) : null;
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
            File.WriteAllText(WatchStatePath, JsonSerializer.Serialize(state, RkzfpsJson.Options));
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
        if (Rkzfps.Core.Benchmark.WeeklySummary.Build(Ctx.Gameplay.All(), now) is not { } summary)
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
        catch (Exception ex) when (ex is Rkzfps.Client.ApiException or IOException or System.Text.Json.JsonException)
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
