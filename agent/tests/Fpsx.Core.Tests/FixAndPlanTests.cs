using Fpsx.Core.Engine;
using Fpsx.Core.Games;
using Fpsx.Core.Model;
using Fpsx.Core.Optimizations;

namespace Fpsx.Core.Tests;

public class FixTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static OptimizationResult Result(ScanResult scan, string id) => scan.Optimizations.Single(o => o.Definition.Id == id);

    [Fact]
    public void PC_saudavel_nao_recebe_nenhuma_correcao()
    {
        var scan = TestData.Engine().Evaluate(Pc.Healthy(), "gaming", "ultimate");
        foreach (var id in new[] { "pagefile-restore-automatic", "background-process-close", "temp-files-cleanup", "game-settings-fix" })
            Assert.DoesNotContain(Result(scan, id).Decision, new[] { Decision.Recommended, Decision.Optional });
    }

    [Fact]
    public void Diagnostico_aponta_a_correcao_que_resolve()
    {
        var s = Pc.Healthy() with
        {
            Memory = new MemoryInfo { TotalBytes = 8 * Gb, AvailableBytes = 5 * Gb, PagefilePresent = false },
            Processes = [new ProcessSample { Pid = 500, Name = "chrome", CpuPercent = 22, WorkingSetBytes = Gb }],
        };
        var scan = TestData.Engine().Evaluate(s, "gaming", "ultimate");
        Assert.Contains(scan.Findings, f => f.FixOptimizationId == "pagefile-restore-automatic");
        Assert.Contains(scan.Findings, f => f.FixOptimizationId == "background-process-close");
    }

    [Fact]
    public void Pagefile_desligado_volta_para_gerenciado_pelo_sistema_e_rollback_restaura()
    {
        var sys = new FakeSystem();
        var off = new RegValue(RegistryKind.MultiString, "");
        sys.WriteRegistry(RegistryRoot.LocalMachine, RegistryPaths.MemoryManagement, "PagingFiles", off);
        var store = new SessionStore(TestData.TempDir());
        var s = Pc.Healthy() with { IsElevated = true, Memory = new MemoryInfo { TotalBytes = 16 * Gb, AvailableBytes = 8 * Gb, PagefilePresent = false } };
        var scan = TestData.Engine().Evaluate(s, "gaming", "ultimate");

        var session = new OptimizationEngine(sys, store).Apply(scan, ["pagefile-restore-automatic"], new ApplyOptions());
        Assert.Equal(ChangeStatus.Applied, Assert.Single(session.Changes).Status);
        Assert.Equal(@"?:\pagefile.sys", sys.ReadRegistry(RegistryRoot.LocalMachine, RegistryPaths.MemoryManagement, "PagingFiles")!.Data);

        new RollbackManager(sys, store).RollbackSession(session.Id, force: false);
        Assert.Equal("", sys.ReadRegistry(RegistryRoot.LocalMachine, RegistryPaths.MemoryManagement, "PagingFiles")!.Data);
    }

    [Fact]
    public void Pagefile_sem_admin_fica_bloqueado()
    {
        var s = Pc.Healthy() with { Memory = new MemoryInfo { TotalBytes = 16 * Gb, AvailableBytes = 8 * Gb, PagefilePresent = false } };
        Assert.Equal(Decision.Blocked, Result(TestData.Engine().Evaluate(s, "gaming", "ultimate"), "pagefile-restore-automatic").Decision);
    }

    [Fact]
    public void Fechar_processo_so_oferece_programa_do_usuario()
    {
        var s = Pc.Healthy() with
        {
            Processes =
            [
                new ProcessSample { Pid = 10, Name = "chrome", CpuPercent = 30, WorkingSetBytes = Gb },
                new ProcessSample { Pid = 11, Name = "svchost", CpuPercent = 40 },
                new ProcessSample { Pid = 12, Name = "MsMpEng", CpuPercent = 25 },
                new ProcessSample { Pid = 13, Name = "steam", CpuPercent = 15 },
                new ProcessSample { Pid = 14, Name = "cs2", CpuPercent = 60 },
                new ProcessSample { Pid = 15, Name = "vgtray", CpuPercent = 9 },
                new ProcessSample { Pid = 16, Name = "notepad", CpuPercent = 0.2 },
            ],
        };
        var close = Result(TestData.Engine().Evaluate(s, "gaming", "ultimate"), "background-process-close");
        Assert.Equal(Decision.Recommended, close.Decision);
        Assert.False(close.AutoSelected);
        var proposal = Assert.Single(close.Evaluation.Proposals);
        Assert.Equal("background-process-close:10", proposal.Id);
    }

    [Fact]
    public void Fechamento_confere_o_nome_do_PID_e_nunca_forca()
    {
        var sys = new FakeSystem();
        sys.Processes[10] = "chrome";
        var exec = new ChangeExecutor(sys);
        Assert.Equal("programa fechado", exec.Apply(new ProcessCloseChange(10, "chrome")));

        sys.Processes[20] = "word";
        var reused = Assert.Throws<InvalidOperationException>(() => exec.Apply(new ProcessCloseChange(20, "chrome")));
        Assert.Contains("agora pertence a word", reused.Message);
        Assert.DoesNotContain("close 20", sys.Log);

        sys.Processes[30] = "editor";
        sys.Stubborn.Add(30);
        Assert.Throws<InvalidOperationException>(() => exec.Apply(new ProcessCloseChange(30, "editor")));
        Assert.Equal("editor", sys.ProcessName(30));
    }

    [Theory]
    [InlineData("svchost")]
    [InlineData("explorer")]
    [InlineData("MsMpEng")]
    [InlineData("steam")]
    [InlineData("cs2")]
    [InlineData("vgtray")]
    [InlineData("fpsx")]
    [InlineData("FC27")]
    [InlineData("FIFA23")]
    public void Politica_recusa_fechar_processo_protegido(string name)
    {
        Assert.Throws<SafetyViolationException>(() => SafetyPolicy.Validate(new ProcessCloseChange(1234, name)));
    }

    [Fact]
    public void Limpeza_de_temporarios_so_aparece_com_disco_cheio()
    {
        var full = Pc.Healthy() with { Disks = [Pc.Healthy().Disks[0] with { TotalBytes = 256 * Gb, FreeBytes = 8 * Gb }] };
        var r = Result(TestData.Engine().Evaluate(full, "gaming", "ultimate"), "temp-files-cleanup");
        Assert.Equal(Decision.Recommended, r.Decision);
        Assert.False(r.AutoSelected);
        Assert.Contains(TestData.Engine().Evaluate(full, "gaming", "ultimate").Findings, f => f.FixOptimizationId == "temp-files-cleanup");
    }

    private static SystemSnapshot Cs2Misconfigured() => Pc.WithCs2(Pc.Healthy(), new()
    {
        ["setting.mat_vsync"] = "1",
        ["setting.r_low_latency"] = "0",
        ["setting.refreshrate_numerator"] = "60",
        ["setting.refreshrate_denominator"] = "1",
    });

    [Fact]
    public void Correcao_do_CS2_propoe_cada_item_encontrado()
    {
        var fix = Result(TestData.Engine().Evaluate(Cs2Misconfigured(), "competitive", "ultimate"), "game-settings-fix");
        Assert.Equal(Decision.Recommended, fix.Decision);
        var ids = fix.Evaluation.Proposals.Select(p => p.Id).ToList();
        Assert.Contains("game-settings-fix:cs2:setting.mat_vsync", ids);
        Assert.Contains("game-settings-fix:cs2:setting.r_low_latency", ids);
        Assert.Contains("game-settings-fix:cs2:refresh", ids);
    }

    [Fact]
    public void Correcao_do_CS2_exige_plano_pro()
    {
        Assert.Equal(Decision.Blocked, Result(TestData.Engine().Evaluate(Cs2Misconfigured(), "gaming", "starter"), "game-settings-fix").Decision);
    }

    [Fact]
    public void Correcao_do_CS2_aplica_com_jogo_fechado_e_desfaz()
    {
        var sys = new FakeSystem();
        foreach (var (k, v) in new Dictionary<string, string> { ["setting.mat_vsync"] = "1", ["setting.r_low_latency"] = "0", ["setting.refreshrate_numerator"] = "60", ["setting.refreshrate_denominator"] = "1" })
            sys.GameConfig[k] = v;
        var store = new SessionStore(TestData.TempDir());
        var scan = TestData.Engine().Evaluate(Cs2Misconfigured(), "competitive", "ultimate");

        sys.GameRunning = true;
        var blocked = new OptimizationEngine(sys, store).Apply(scan, ["game-settings-fix:cs2:setting.mat_vsync"], new ApplyOptions());
        Assert.Contains("Feche o jogo", Assert.Single(blocked.Changes).Error);
        Assert.Equal("1", sys.GameConfig["setting.mat_vsync"]);

        sys.GameRunning = false;
        var session = new OptimizationEngine(sys, store).Apply(scan, ["game-settings-fix"], new ApplyOptions());
        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.Equal("0", sys.GameConfig["setting.mat_vsync"]);
        Assert.Equal("144", sys.GameConfig["setting.refreshrate_numerator"]);

        new RollbackManager(sys, store).RollbackSession(session.Id, force: false);
        Assert.Equal("1", sys.GameConfig["setting.mat_vsync"]);
        Assert.Equal("60", sys.GameConfig["setting.refreshrate_numerator"]);
    }

    [Theory]
    [InlineData("cs2", "setting.fps_max", "0")]
    [InlineData("cs2", "setting.mat_vsync", "1; rm -rf")]
    [InlineData("valorant", "setting.mat_vsync", "0")]
    public void Politica_recusa_chave_de_jogo_fora_da_whitelist(string game, string key, string value)
    {
        Assert.Throws<SafetyViolationException>(() => SafetyPolicy.Validate(new GameConfigChange(game, key, value)));
    }

    [Fact]
    public void Troca_de_valor_preserva_o_resto_do_arquivo()
    {
        const string text = "\"video.cfg\"\n{\n\t\"setting.mat_vsync\"\t\t\"1\"\n\t\"setting.defaultres\"\t\t\"1920\"\n}\n";
        var updated = ValveFiles.ReplaceValue(text, "setting.mat_vsync", "0")!;
        Assert.Equal(text.Replace("\"1\"\n", "\"0\"\n"), updated);
        Assert.Null(ValveFiles.ReplaceValue(text, "setting.nao_existe", "0"));
    }

    [Theory]
    [InlineData(@"?:\pagefile.sys", true)]
    [InlineData("C:\\pagefile.sys 4096 8192", true)]
    [InlineData("", true)]
    [InlineData(@"C:\Windows\System32\evil.dll", false)]
    public void PagingFiles_aceita_so_o_formato_do_windows(string data, bool ok)
    {
        var change = new RegistryValueChange(RegistryRoot.LocalMachine, RegistryPaths.MemoryManagement, "PagingFiles", new RegValue(RegistryKind.MultiString, data));
        if (ok)
            SafetyPolicy.Validate(change);
        else
            Assert.Throws<SafetyViolationException>(() => SafetyPolicy.Validate(change));
        Assert.Throws<SafetyViolationException>(() => SafetyPolicy.Validate(change with { Value = null }));
    }
}

public class PlanFeatureTests
{
    [Fact]
    public void Cada_plano_contem_tudo_do_anterior()
    {
        var previous = new HashSet<Feature>();
        foreach (var plan in Plans.Order)
        {
            var current = PlanFeatures.For(plan).ToHashSet();
            Assert.Superset(previous, current);
            previous = current;
        }

        Assert.Equal(Enum.GetValues<Feature>().Length, previous.Count);
    }

    [Fact]
    public void Desfazer_e_liberado_em_todos_os_planos()
    {
        Assert.All(Plans.Order, p => Assert.True(PlanFeatures.Allows(p, Feature.Rollback)));
    }

    [Fact]
    public void Catalogo_respeita_a_matriz_de_planos()
    {
        foreach (var o in TestData.Catalog().Optimizations.Where(o => o.Classification != Classification.NotRecommended))
        {
            if (o.Classification == Classification.Experimental)
                Assert.Equal("ultimate", o.MinPlan);
            if (o.Id is "pagefile-restore-automatic" or "background-process-close" or "temp-files-cleanup")
                Assert.True(Plans.Rank(o.MinPlan) >= Plans.Rank(PlanFeatures.RequiredPlan(Feature.ProblemFixes)), o.Id);
            if (o.Category == "game")
                Assert.True(Plans.Rank(o.MinPlan) >= Plans.Rank(PlanFeatures.RequiredPlan(Feature.GameProfiles)), o.Id);
            if (o.Id == "startup-entry-disable")
                Assert.Equal(PlanFeatures.RequiredPlan(Feature.Startup), o.MinPlan);
        }
    }
}
