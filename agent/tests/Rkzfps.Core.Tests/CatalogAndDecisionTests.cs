using Rkzfps.Core.Catalog;
using Rkzfps.Core.Engine;
using Rkzfps.Core.Model;
using Rkzfps.Core.Optimizations;

namespace Rkzfps.Core.Tests;

public class CatalogTests
{
    [Fact]
    public void Catalogo_real_bate_com_os_handlers_compilados()
    {
        var errors = CatalogValidator.Validate(TestData.Catalog(), OptimizationRegistry.All);
        Assert.Empty(errors);
    }

    [Fact]
    public void Catalogo_com_id_sem_handler_e_recusado()
    {
        var catalog = TestData.Catalog();
        var tampered = catalog with
        {
            Optimizations = [.. catalog.Optimizations, catalog.Optimizations[0] with { Id = "turbo-fps-magico" }],
        };
        Assert.Contains(CatalogValidator.Validate(tampered, OptimizationRegistry.All), e => e.Contains("turbo-fps-magico"));
        Assert.Throws<InvalidDataException>(() => new DecisionEngine(tampered, TestData.Games()).Evaluate(Pc.Healthy(), "gaming", "ultimate"));
    }

    [Fact]
    public void Justificativa_incompleta_e_recusada()
    {
        var catalog = TestData.Catalog();
        var first = catalog.Optimizations[0];
        var tampered = catalog with { Optimizations = [first with { Justification = first.Justification with { Evidence = "" } }, .. catalog.Optimizations.Skip(1)] };
        Assert.Contains(CatalogValidator.Validate(tampered, OptimizationRegistry.All), e => e.Contains("evidence"));
    }

    [Fact]
    public void Not_recommended_nunca_tem_handler()
    {
        var notRecommended = TestData.Catalog().Optimizations.Where(o => o.Classification == Classification.NotRecommended).Select(o => o.Id).ToList();
        Assert.NotEmpty(notRecommended);
        Assert.DoesNotContain(OptimizationRegistry.All, h => notRecommended.Contains(h.Id));
    }

    [Fact]
    public void Perfil_cs2_carrega_com_regras()
    {
        var cs2 = Assert.Single(TestData.Games(), g => g.Id == "cs2");
        Assert.Equal(730, cs2.Detect.SteamAppId);
        Assert.NotEmpty(cs2.SettingChecks);
        Assert.All(cs2.Optimizations, id => Assert.NotNull(TestData.Catalog().Find(id)));
    }
}

public class DecisionTests
{
    private static OptimizationResult Result(ScanResult scan, string id) => scan.Optimizations.Single(o => o.Definition.Id == id);

    [Fact]
    public void PC_ja_otimizado_nao_recebe_nenhuma_alteracao_automatica()
    {
        var scan = TestData.Engine().Evaluate(Pc.Healthy(), "competitive", "ultimate");
        Assert.DoesNotContain(scan.Optimizations, o => o.AutoSelected);
        Assert.Equal(Decision.AlreadyOptimal, Result(scan, "game-mode-enable").Decision);
        Assert.Equal(Decision.AlreadyOptimal, Result(scan, "display-refresh-rate-max").Decision);
        Assert.Equal(Decision.AlreadyOptimal, Result(scan, "power-plan-high-performance").Decision);
    }

    [Fact]
    public void PC_mal_configurado_recebe_as_correcoes_comprovadas()
    {
        var scan = TestData.Engine().Evaluate(Pc.Misconfigured(), "gaming", "ultimate");

        var gameMode = Result(scan, "game-mode-enable");
        Assert.Equal(Decision.Recommended, gameMode.Decision);
        Assert.True(gameMode.AutoSelected);
        var change = Assert.IsType<RegistryValueChange>(Assert.Single(Assert.Single(gameMode.Evaluation.Proposals).Changes));
        Assert.Equal("1", change.Value!.Data);

        Assert.True(Result(scan, "power-plan-leave-power-saver").AutoSelected);
        Assert.True(Result(scan, "display-refresh-rate-max").AutoSelected);
    }

    [Fact]
    public void Gravacao_em_segundo_plano_e_recomendada_mas_nunca_automatica()
    {
        var capture = Result(TestData.Engine().Evaluate(Pc.Misconfigured(), "competitive", "ultimate"), "game-capture-background-recording-disable");
        Assert.Equal(Decision.Recommended, capture.Decision);
        Assert.False(capture.AutoSelected);
        Assert.NotNull(capture.Evaluation.Warning);
    }

    [Fact]
    public void Startup_nunca_propoe_seguranca_nem_driver_e_nunca_e_automatico()
    {
        var startup = Result(TestData.Engine().Evaluate(Pc.Misconfigured(), "low-end", "ultimate"), "startup-entry-disable");
        Assert.Equal(Decision.Optional, startup.Decision);
        Assert.False(startup.AutoSelected);
        var proposal = Assert.Single(startup.Evaluation.Proposals);
        Assert.Equal("startup-entry-disable:Discord", proposal.Id);
    }

    [Fact]
    public void Notebook_na_bateria_nao_troca_plano_de_energia()
    {
        var scan = TestData.Engine().Evaluate(Pc.Laptop(onAc: false), "competitive", "ultimate");
        Assert.Equal(Decision.NotApplicable, Result(scan, "power-plan-high-performance").Decision);
    }

    [Fact]
    public void Notebook_na_tomada_tem_alto_desempenho_so_opcional_com_aviso()
    {
        var hp = Result(TestData.Engine().Evaluate(Pc.Laptop(onAc: true), "competitive", "ultimate"), "power-plan-high-performance");
        Assert.Equal(Decision.Optional, hp.Decision);
        Assert.False(hp.AutoSelected);
        Assert.Contains("temperatura", hp.Evaluation.Warning);
    }

    [Fact]
    public void Economia_de_energia_em_notebook_na_bateria_e_respeitada()
    {
        var s = Pc.Laptop(onAc: false) with { Power = Pc.Laptop(false).Power! with { ActiveSchemeGuid = PowerSchemes.PowerSaver } };
        Assert.Equal(Decision.NotApplicable, Result(TestData.Engine().Evaluate(s, "gaming", "ultimate"), "power-plan-leave-power-saver").Decision);
    }

    [Fact]
    public void Sem_plano_alto_desempenho_nao_inventa_um()
    {
        var s = Pc.Healthy() with
        {
            Power = Pc.Healthy().Power! with { ActiveSchemeGuid = PowerSchemes.Balanced, Schemes = [new PowerScheme(PowerSchemes.Balanced, "Equilibrado")] },
        };
        Assert.Equal(Decision.NotApplicable, Result(TestData.Engine().Evaluate(s, "competitive", "ultimate"), "power-plan-high-performance").Decision);
    }

    [Fact]
    public void Plano_de_energia_desconhecido_vira_unknown_e_nao_altera()
    {
        var s = Pc.Healthy() with { Power = new PowerInfo() };
        Assert.Equal(Decision.Unknown, Result(TestData.Engine().Evaluate(s, "gaming", "ultimate"), "power-plan-leave-power-saver").Decision);
    }

    [Fact]
    public void Experimental_nunca_e_automatico_e_exige_admin_quando_escreve_em_HKLM()
    {
        var engine = TestData.Engine();
        // Sem administrador continua disponível, marcada para o app pedir a
        // permissão do Windows na hora de aplicar (antes ficava bloqueada).
        var notAdmin = Result(engine.Evaluate(Pc.Misconfigured(), "competitive", "ultimate"), "hags-enable");
        Assert.Equal(Decision.Optional, notAdmin.Decision);
        Assert.True(notAdmin.RequiresElevation);

        var admin = Result(engine.Evaluate(Pc.Misconfigured() with { IsElevated = true }, "competitive", "ultimate"), "hags-enable");
        Assert.Equal(Decision.Optional, admin.Decision);
        Assert.False(admin.RequiresElevation);
        Assert.False(admin.AutoSelected);
    }

    [Fact]
    public void Motor_sem_administrador_pula_o_que_exige_permissao_com_motivo()
    {
        var scan = TestData.Engine().Evaluate(Pc.Misconfigured(), "competitive", "ultimate");
        var session = new OptimizationEngine(new FakeSystem(), new SessionStore(TestData.TempDir()))
            .Apply(scan, ["hags-enable"], new ApplyOptions { AllowExperimental = true });
        Assert.Empty(session.Changes);
        Assert.Contains("administrador", Assert.Single(session.Skipped).Reason);
    }

    [Fact]
    public void Plano_free_so_enxerga_e_mostra_o_que_cada_otimizacao_resolveria()
    {
        var scan = TestData.Engine().Evaluate(Pc.Misconfigured(), "gaming", "free");
        Assert.DoesNotContain(scan.Optimizations, o => o.Decision is Decision.Recommended or Decision.Optional);
        var gameMode = Result(scan, "game-mode-enable");
        Assert.Equal(Decision.Blocked, gameMode.Decision);
        Assert.Contains("Game Mode foi desativado", gameMode.Reason);
        Assert.Contains("plano Starter", gameMode.Reason);
        Assert.NotEmpty(gameMode.Evaluation.Proposals);
        Assert.NotEmpty(scan.Findings);
        Assert.Equal(Decision.Recommended, Result(TestData.Engine().Evaluate(Pc.Misconfigured(), "gaming", "starter"), "game-mode-enable").Decision);
    }

    [Fact]
    public void Otimizacao_desativada_pelo_admin_fica_bloqueada()
    {
        var catalog = TestData.Catalog();
        var disabled = catalog with { Optimizations = catalog.Optimizations.Select(o => o.Id == "game-mode-enable" ? o with { Enabled = false } : o).ToList() };
        var scan = new DecisionEngine(disabled, TestData.Games()).Evaluate(Pc.Misconfigured(), "gaming", "ultimate");
        Assert.Equal(Decision.Blocked, Result(scan, "game-mode-enable").Decision);
    }

    [Fact]
    public void Perfil_custom_nao_seleciona_nada_sozinho()
    {
        var scan = TestData.Engine().Evaluate(Pc.Misconfigured(), "custom", "ultimate");
        Assert.DoesNotContain(scan.Optimizations, o => o.AutoSelected);
    }

    [Fact]
    public void Perfil_streaming_nao_mexe_em_captura()
    {
        var scan = TestData.Engine().Evaluate(Pc.Misconfigured(), "streaming", "ultimate");
        Assert.False(Result(scan, "game-capture-background-recording-disable").AutoSelected);
    }

    [Fact]
    public void Troubleshooting_e_sempre_opcional_com_potencial_nenhum()
    {
        var scan = TestData.Engine().Evaluate(Pc.Healthy(), "gaming", "ultimate");
        foreach (var id in new[] { "shader-cache-clear-directx", "dns-flush" })
        {
            var r = Result(scan, id);
            Assert.Equal(Decision.Optional, r.Decision);
            Assert.Equal(Potential.None, r.Evaluation.Potential);
            Assert.False(r.AutoSelected);
        }

        Assert.Equal(Decision.NotApplicable, Result(scan, "shader-cache-clear-cs2").Decision);
    }

    [Theory]
    [MemberData(nameof(Pc.Matrix), MemberType = typeof(Pc))]
    public void Matriz_de_PCs_simulados_respeita_as_regras(string name, SystemSnapshot snapshot)
    {
        Assert.False(string.IsNullOrEmpty(name));
        var catalog = TestData.Catalog();
        foreach (var profile in catalog.Profiles)
        {
            var scan = TestData.Engine().Evaluate(snapshot, profile.Id, "ultimate");
            foreach (var o in scan.Optimizations.Where(o => o.AutoSelected))
            {
                Assert.Equal(Decision.Recommended, o.Decision);
                Assert.Contains(o.Definition.Classification, new[] { Classification.Proven, Classification.Conditional });
                Assert.Equal(RiskLevel.Low, o.Definition.Risk);
                Assert.False(o.Definition.RequiresUserConfirmation);
            }

            Assert.DoesNotContain(scan.Optimizations, o => o.Definition.Classification == Classification.NotRecommended);
            Assert.DoesNotContain(scan.Findings, f => f.Title.StartsWith("Falha no diagnóstico"));

            // Notebook nunca recebe Alto desempenho automaticamente.
            if (snapshot.Power!.HasBattery)
                Assert.False(scan.Optimizations.Single(o => o.Definition.Id == "power-plan-high-performance").AutoSelected);
        }
    }
}
