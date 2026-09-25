using Fpsx.Core.Engine;
using Fpsx.Core.Model;
using Fpsx.Core.Optimizations;

namespace Fpsx.Core.Tests;

public class EngineTests
{
    private static (FakeSystem Sys, SessionStore Store, OptimizationEngine Engine, ScanResult Scan) Setup(SystemSnapshot? snapshot = null)
    {
        var sys = new FakeSystem();
        sys.Registry[(RegistryRoot.CurrentUser, RegistryPaths.GameBar.ToLowerInvariant(), "autogamemodeenabled")] = RegValue.DWord(0);
        sys.ActiveScheme = PowerSchemes.PowerSaver;
        sys.Displays[@"\\.\DISPLAY1"] = new DisplayMode(1920, 1080, 60);
        var store = new SessionStore(TestData.TempDir());
        var scan = TestData.Engine().Evaluate(snapshot ?? Pc.Misconfigured(), "gaming", "ultimate");
        return (sys, store, new OptimizationEngine(sys, store), scan);
    }

    private static List<string> Auto(ScanResult scan) =>
        scan.Optimizations.Where(o => o.AutoSelected).SelectMany(o => o.Evaluation.Proposals.Select(p => p.Id)).ToList();

    [Fact]
    public void Aplica_verifica_e_grava_backup_antes_de_cada_alteracao()
    {
        var (sys, store, engine, scan) = Setup();
        var session = engine.Apply(scan, Auto(scan), new ApplyOptions());

        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.All(session.Changes, c => Assert.Equal(ChangeStatus.Applied, c.Status));
        Assert.All(session.Changes, c => Assert.True(c.Verified));
        Assert.Equal("1", sys.ReadRegistry(RegistryRoot.CurrentUser, RegistryPaths.GameBar, "AutoGameModeEnabled")!.Data);
        Assert.Equal(PowerSchemes.Balanced, sys.ActiveScheme);
        Assert.Equal(144, sys.Displays[@"\\.\DISPLAY1"].RefreshHz);

        var saved = store.Load(session.Id)!;
        var gameMode = saved.Changes.Single(c => c.OptimizationId == "game-mode-enable");
        Assert.Equal("0", Assert.IsType<RegistryValueChange>(gameMode.Inverse).Value!.Data);
        Assert.Contains("game-capture-background-recording-disable", scan.Optimizations.Where(o => !o.AutoSelected).Select(o => o.Definition.Id));
        Assert.True(File.ReadAllLines(store.LogPath).Length >= 3);
    }

    [Fact]
    public void Rollback_da_sessao_restaura_o_estado_anterior()
    {
        var (sys, store, engine, scan) = Setup();
        var session = engine.Apply(scan, Auto(scan), new ApplyOptions());

        var rolled = new RollbackManager(sys, store).RollbackSession(session.Id, force: false);

        Assert.Equal(SessionStatus.RolledBack, rolled.Status);
        Assert.Equal("0", sys.ReadRegistry(RegistryRoot.CurrentUser, RegistryPaths.GameBar, "AutoGameModeEnabled")!.Data);
        Assert.Equal(PowerSchemes.PowerSaver, sys.ActiveScheme);
        Assert.Equal(60, sys.Displays[@"\\.\DISPLAY1"].RefreshHz);
    }

    [Fact]
    public void Rollback_remove_valor_que_nao_existia_antes()
    {
        var (sys, store, engine, scan) = Setup();
        var session = engine.Apply(scan, ["game-capture-background-recording-disable"], new ApplyOptions());
        Assert.Equal("0", sys.ReadRegistry(RegistryRoot.CurrentUser, RegistryPaths.GameDvr, "HistoricalCaptureEnabled")!.Data);

        new RollbackManager(sys, store).RollbackSession(session.Id, force: false);
        Assert.Null(sys.ReadRegistry(RegistryRoot.CurrentUser, RegistryPaths.GameDvr, "HistoricalCaptureEnabled"));
    }

    [Fact]
    public void Rollback_de_uma_alteracao_so_desfaz_ela()
    {
        var (sys, store, engine, scan) = Setup();
        var session = engine.Apply(scan, Auto(scan), new ApplyOptions());
        var power = session.Changes.Single(c => c.OptimizationId == "power-plan-leave-power-saver");

        new RollbackManager(sys, store).RollbackChange(session.Id, power.Id, force: false);

        Assert.Equal(PowerSchemes.PowerSaver, sys.ActiveScheme);
        Assert.Equal("1", sys.ReadRegistry(RegistryRoot.CurrentUser, RegistryPaths.GameBar, "AutoGameModeEnabled")!.Data);
    }

    [Fact]
    public void Rollback_nao_atropela_mudanca_feita_depois_pelo_usuario()
    {
        var (sys, store, engine, scan) = Setup();
        var session = engine.Apply(scan, Auto(scan), new ApplyOptions());
        sys.ActiveScheme = PowerSchemes.HighPerformance; // o usuário trocou depois

        var rolled = new RollbackManager(sys, store).RollbackSession(session.Id, force: false);
        Assert.Equal(PowerSchemes.HighPerformance, sys.ActiveScheme);
        Assert.Equal(ChangeStatus.RollbackSkipped, rolled.Changes.Single(c => c.OptimizationId == "power-plan-leave-power-saver").Status);

        new RollbackManager(sys, store).RollbackSession(session.Id, force: true);
        Assert.Equal(PowerSchemes.PowerSaver, sys.ActiveScheme);
    }

    [Fact]
    public void Falha_para_tudo_por_padrao_e_nao_continua_em_silencio()
    {
        var (sys, _, engine, scan) = Setup();
        sys.FailWrites.Add("AutoGameModeEnabled");

        var session = engine.Apply(scan, Auto(scan), new ApplyOptions());

        Assert.Equal(SessionStatus.Cancelled, session.Status);
        Assert.Equal(ChangeStatus.Failed, Assert.Single(session.Changes).Status);
        Assert.NotEmpty(session.Skipped);
        Assert.Equal(PowerSchemes.PowerSaver, sys.ActiveScheme);
    }

    [Fact]
    public void Falha_com_restaurar_desfaz_o_que_ja_foi_aplicado()
    {
        var (sys, _, engine, scan) = Setup();
        sys.IgnoreWrites.Add("AutoGameModeEnabled"); // escrita "não pega": só a verificação percebe
        var ids = Auto(scan).OrderBy(id => id.StartsWith("game-mode") ? 1 : 0).ToList(); // energia e monitor primeiro
        FailureContext? seen = null;

        var session = engine.Apply(scan, ids, new ApplyOptions { OnFailure = f => { seen = f; return FailureChoice.Restore; } });

        Assert.NotNull(seen);
        Assert.Contains("Verificação falhou", seen!.Error);
        Assert.Equal(SessionStatus.RolledBack, session.Status);
        Assert.Equal(PowerSchemes.PowerSaver, sys.ActiveScheme);
        Assert.Equal(60, sys.Displays[@"\\.\DISPLAY1"].RefreshHz);
    }

    [Fact]
    public void Falha_com_continuar_segue_para_as_proximas()
    {
        var (sys, _, engine, scan) = Setup();
        sys.FailWrites.Add("AutoGameModeEnabled");
        var ids = Auto(scan).OrderBy(id => id.StartsWith("game-mode") ? 0 : 1).ToList();

        var session = engine.Apply(scan, ids, new ApplyOptions { OnFailure = _ => FailureChoice.Continue });

        Assert.Equal(SessionStatus.Failed, session.Status);
        Assert.Equal(PowerSchemes.Balanced, sys.ActiveScheme);
    }

    [Fact]
    public void Experimental_exige_autorizacao_explicita()
    {
        var (sys, _, engine, scan) = Setup(Pc.Misconfigured() with { IsElevated = true });
        var without = engine.Apply(scan, ["hags-enable"], new ApplyOptions());
        Assert.Empty(without.Changes);
        Assert.Contains(without.Skipped, s => s.Reason.Contains("Experimental"));

        var with = engine.Apply(scan, ["hags-enable"], new ApplyOptions { AllowExperimental = true });
        Assert.Equal(ChangeStatus.Applied, Assert.Single(with.Changes).Status);
        Assert.Contains(sys.Log, l => l.Contains("HwSchMode=2"));
    }

    [Fact]
    public void Item_ja_otimizado_e_ignorado_com_motivo()
    {
        var (sys, _, engine, _) = Setup();
        var scan = TestData.Engine().Evaluate(Pc.Healthy(), "gaming", "ultimate");
        var session = engine.Apply(scan, ["game-mode-enable"], new ApplyOptions());
        Assert.Empty(session.Changes);
        Assert.Empty(sys.Log);
    }

    [Fact]
    public void Dry_run_nao_toca_no_sistema()
    {
        var (sys, _, engine, scan) = Setup();
        engine.Apply(scan, Auto(scan), new ApplyOptions { DryRun = true });
        Assert.Empty(sys.Log);
    }

    [Fact]
    public void Troubleshooting_sem_rollback_fica_registrado_como_mantido()
    {
        var (sys, store, engine, scan) = Setup();
        var session = engine.Apply(scan, ["dns-flush"], new ApplyOptions());
        Assert.Contains("net FlushDns", sys.Log);

        var rolled = new RollbackManager(sys, store).RollbackSession(session.Id, force: false);
        Assert.Equal(ChangeStatus.RollbackSkipped, Assert.Single(rolled.Changes).Status);
    }
}

public class SafetyTests
{
    [Theory]
    [InlineData(RegistryRoot.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiSpyware")]
    [InlineData(RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile", "EnableFirewall")]
    [InlineData(RegistryRoot.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate")]
    [InlineData(RegistryRoot.CurrentUser, @"Software\Microsoft\GameBar", "QualquerOutroValor")]
    [InlineData(RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDelay")]
    [InlineData(RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation")]
    public void Chaves_fora_da_whitelist_sao_recusadas(RegistryRoot root, string path, string name)
    {
        Assert.Throws<SafetyViolationException>(() => SafetyPolicy.Validate(new RegistryValueChange(root, path, name, RegValue.DWord(1))));
    }

    [Fact]
    public void Nao_desliga_item_de_seguranca_nem_por_pedido_explicito()
    {
        var change = new RegistryValueChange(RegistryRoot.LocalMachine, RegistryPaths.StartupApproved + @"\Run", "SecurityHealth", StartupOptimization.DisabledValue(DateTimeOffset.Now));
        Assert.Throws<SafetyViolationException>(() => SafetyPolicy.Validate(change));
    }

    [Fact]
    public void Executor_valida_antes_de_escrever()
    {
        var sys = new FakeSystem();
        var executor = new ChangeExecutor(sys);
        Assert.Throws<SafetyViolationException>(() => executor.Apply(new RegistryValueChange(RegistryRoot.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiSpyware", RegValue.DWord(1))));
        Assert.Empty(sys.Log);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(5000)]
    public void Taxa_de_atualizacao_absurda_e_recusada(int hz)
    {
        Assert.Throws<SafetyViolationException>(() => SafetyPolicy.Validate(new DisplayRefreshChange(@"\\.\DISPLAY1", 1920, 1080, hz)));
    }

    [Fact]
    public void Valor_de_startup_desligado_segue_o_formato_do_gerenciador_de_tarefas()
    {
        var v = StartupOptimization.DisabledValue(new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(RegistryKind.Binary, v.Kind);
        Assert.Equal(24, v.Data.Length);
        Assert.StartsWith("03000000", v.Data);
        Assert.False(StartupOptimization.IsEnabledRaw(v.Data));
        Assert.True(StartupOptimization.IsEnabledRaw("020000000000000000000000"));
        Assert.True(StartupOptimization.IsEnabledRaw(null));
    }
}
