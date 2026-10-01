using Rkzfps.Core.Engine;
using Rkzfps.Core.Model;
using Rkzfps.Core.Optimizations;

namespace Rkzfps.Core.Tests;

public class GamingModeTests
{
    private const string LeaveSaver = "power-plan-leave-power-saver";

    // PC na tomada com "Economia de energia": o caso em que o Automático tem o que fazer.
    private static ScanResult Scan() => TestData.Engine().Evaluate(Pc.Misconfigured(), "gaming", "ultimate");

    private static (FakeSystem Sys, SessionStore Store, GamingSessionManager Manager) Setup()
    {
        var sys = new FakeSystem { ActiveScheme = PowerSchemes.PowerSaver };
        var store = new SessionStore(TestData.TempDir());
        return (sys, store, new GamingSessionManager(sys, store));
    }

    [Fact]
    public void Manual_nunca_aplica_nada()
    {
        var (sys, store, m) = Setup();
        var r = m.Start("Counter-Strike 2", Scan(), GamingModeKind.Manual, [LeaveSaver]);
        Assert.False(r.Applied);
        Assert.Empty(sys.Log);
        Assert.Empty(store.All());
        Assert.Contains("Manual", r.Message);
    }

    [Fact]
    public void Automatico_sem_autorizacao_nao_aplica()
    {
        var (sys, store, m) = Setup();
        var r = m.Start("Counter-Strike 2", Scan(), GamingModeKind.Automatic, []);
        Assert.False(r.Applied);
        Assert.Empty(sys.Log);
        Assert.Empty(store.All());
    }

    [Fact]
    public void Inicio_aplica_so_o_autorizado_e_fim_restaura()
    {
        var (sys, store, m) = Setup();
        var start = m.Start("Counter-Strike 2", Scan(), GamingModeKind.Automatic, [LeaveSaver]);

        Assert.True(start.Applied);
        Assert.Equal(PowerSchemes.Balanced, sys.ActiveScheme);
        Assert.Equal("Counter-Strike 2", start.Session!.GamingGame);
        Assert.All(start.Session.Changes, c => Assert.Equal(LeaveSaver, c.OptimizationId));
        Assert.Single(m.Pending());

        var restored = m.Restore();
        Assert.Single(restored);
        Assert.Equal(PowerSchemes.PowerSaver, sys.ActiveScheme);
        Assert.Empty(m.Pending());
        // O histórico continua lá, com a sessão marcada como desfeita.
        var saved = Assert.Single(store.All());
        Assert.Equal(SessionStatus.RolledBack, saved.Status);
        Assert.All(saved.Changes, c => Assert.Equal(ChangeStatus.RolledBack, c.Status));
    }

    [Fact]
    public void Autorizar_tudo_nao_vira_aplicar_tudo()
    {
        var (sys, store, m) = Setup();
        var scan = Scan();
        // Mesmo com TODAS as otimizações do catálogo marcadas como autorizadas.
        var everything = scan.Optimizations.Select(o => o.Definition.Id).ToList();
        m.Start("Counter-Strike 2", scan, GamingModeKind.Automatic, everything);

        var applied = store.All().SelectMany(s => s.Changes).Select(c => c.Applied).ToList();
        Assert.NotEmpty(applied);
        Assert.All(applied, c => Assert.IsType<PowerSchemeChange>(c));
        Assert.DoesNotContain(sys.Log, l => l.StartsWith("close", StringComparison.Ordinal) || l.StartsWith("write", StringComparison.Ordinal) || l.StartsWith("display", StringComparison.Ordinal));
    }

    [Fact]
    public void Nenhuma_acao_automatica_fecha_programa()
    {
        var scan = Scan();
        // Fechar programa existe no catálogo, mas nunca é elegível ao Automático.
        Assert.All(scan.Optimizations.Where(o => o.Evaluation.Proposals.Any(p => p.Changes.Any(c => c is ProcessCloseChange))),
            o => Assert.False(GamingPolicy.IsEligible(o)));
        Assert.DoesNotContain(GamingPolicy.Candidates(scan, []), c => c.OptimizationId == "background-process-close");
        Assert.False(GamingPolicy.IsTemporaryChange(new ProcessCloseChange(123, "chrome")));
        Assert.False(GamingPolicy.IsTemporaryChange(new GameConfigChange("cs2", "setting.mat_vsync", "0")));
        Assert.False(GamingPolicy.IsTemporaryChange(new CacheClearChange(CacheTarget.UserTemp)));
        Assert.False(GamingPolicy.IsTemporaryChange(new RegistryValueChange(RegistryRoot.LocalMachine, "SOFTWARE\\X", "Y", RegValue.DWord(1))));
    }

    [Fact]
    public void Sem_permissao_de_administrador_nao_entra()
    {
        var (sys, store, m) = Setup();
        var scan = Scan();
        var needsAdmin = scan with
        {
            Optimizations = scan.Optimizations.Select(o => o.Definition.Id == LeaveSaver ? o with { RequiresElevation = true } : o).ToList(),
        };
        var r = m.Start("Counter-Strike 2", needsAdmin, GamingModeKind.Automatic, [LeaveSaver]);
        Assert.False(r.Applied);
        Assert.Equal(PowerSchemes.PowerSaver, sys.ActiveScheme);
        Assert.Empty(store.All());
    }

    [Fact]
    public void Falha_na_aplicacao_desfaz_e_deixa_o_pc_como_estava()
    {
        var (sys, store, m) = Setup();
        var scan = Scan();
        var leave = scan.Optimizations.Single(o => o.Definition.Id == LeaveSaver);
        // Segunda otimização elegível cuja troca de plano falha (sem permissão).
        var failing = leave with
        {
            Definition = leave.Definition with { Id = "power-plan-failing" },
            Evaluation = leave.Evaluation with { Proposals = [new Proposal("power-plan-failing", "Plano X", [new PowerSchemeChange(PowerSchemes.HighPerformance, "Alto desempenho")], Potential.Low, "")] },
        };
        sys.FailPowerSchemes.Add(PowerSchemes.HighPerformance);
        var withFailure = scan with { Optimizations = [.. scan.Optimizations, failing] };

        var r = m.Start("Counter-Strike 2", withFailure, GamingModeKind.Automatic, [LeaveSaver, "power-plan-failing"]);

        Assert.False(r.Applied);
        Assert.Contains("falhou", r.Message);
        Assert.Equal(PowerSchemes.PowerSaver, sys.ActiveScheme);
        Assert.Equal(SessionStatus.RolledBack, r.Session!.Status);
        Assert.Empty(m.Pending());
    }

    [Fact]
    public void App_reaberto_depois_de_cair_com_o_jogo_aberto_restaura()
    {
        var (sys, store, m) = Setup();
        m.Start("Counter-Strike 2", Scan(), GamingModeKind.Automatic, [LeaveSaver]);
        Assert.Equal(PowerSchemes.Balanced, sys.ActiveScheme);

        // Processo do app morreu: um gerenciador novo, só com o que está no disco.
        var afterRestart = new GamingSessionManager(sys, new SessionStore(StoreDir(store)));
        Assert.Single(afterRestart.Pending());
        afterRestart.Restore();
        Assert.Equal(PowerSchemes.PowerSaver, sys.ActiveScheme);
        Assert.Empty(afterRestart.Pending());
    }

    [Fact]
    public void Queda_no_meio_da_aplicacao_tambem_restaura()
    {
        var (sys, store, m) = Setup();
        // Sessão temporária com a alteração ainda "pendente": o app caiu entre o
        // backup e a confirmação, mas a troca de plano chegou a acontecer.
        sys.ActiveScheme = PowerSchemes.Balanced;
        store.Save(new SessionRecord
        {
            Id = "crash", StartedAt = DateTimeOffset.Now, Status = SessionStatus.Running, GamingGame = "Counter-Strike 2",
            Changes =
            [
                new ChangeRecord
                {
                    Id = "c1", OptimizationId = LeaveSaver, Status = ChangeStatus.Pending,
                    Applied = new PowerSchemeChange(PowerSchemes.Balanced, "Equilibrado"),
                    Inverse = new PowerSchemeChange(PowerSchemes.PowerSaver, "Economia de energia"),
                },
            ],
        });

        m.Restore();
        Assert.Equal(PowerSchemes.PowerSaver, sys.ActiveScheme);
        Assert.Empty(m.Pending());
    }

    [Fact]
    public void Escolha_da_pessoa_durante_o_jogo_e_respeitada()
    {
        var (sys, store, m) = Setup();
        m.Start("Counter-Strike 2", Scan(), GamingModeKind.Automatic, [LeaveSaver]);
        // A pessoa trocou o plano no meio da partida.
        sys.ActiveScheme = PowerSchemes.HighPerformance;

        var restored = Assert.Single(m.Restore());
        Assert.Equal(PowerSchemes.HighPerformance, sys.ActiveScheme);
        Assert.All(restored.Changes, c => Assert.Equal(ChangeStatus.RollbackSkipped, c.Status));
        Assert.Empty(m.Pending());
    }

    [Fact]
    public void Novo_jogo_desfaz_a_sobra_do_anterior_antes()
    {
        var (sys, store, m) = Setup();
        m.Start("Counter-Strike 2", Scan(), GamingModeKind.Automatic, [LeaveSaver]);
        // O fim do jogo anterior não chegou (app caiu); outro jogo abre.
        m.Start("Valorant", Scan(), GamingModeKind.Automatic, [LeaveSaver]);

        var pending = Assert.Single(m.Pending());
        Assert.Equal("Valorant", pending.GamingGame);
        Assert.Equal(SessionStatus.RolledBack, store.All().Single(s => s.GamingGame == "Counter-Strike 2").Status);
        m.Restore();
        Assert.Equal(PowerSchemes.PowerSaver, sys.ActiveScheme);
    }

    [Fact]
    public void Sessao_temporaria_nao_vira_marco_do_antes_e_depois()
    {
        var (_, store, m) = Setup();
        m.Start("Counter-Strike 2", Scan(), GamingModeKind.Automatic, [LeaveSaver]);
        Assert.Empty(Rkzfps.Core.Benchmark.GameplayComparer.Pivots(store.All()));
    }

    [Fact]
    public void Troca_entre_automatico_e_manual()
    {
        Assert.Equal(GamingModeKind.Automatic, GamingPolicy.Parse("auto"));
        Assert.Equal(GamingModeKind.Manual, GamingPolicy.Parse("manual"));
        // Valor desconhecido ou ausente cai no Manual: na dúvida, nada automático.
        Assert.Equal(GamingModeKind.Manual, GamingPolicy.Parse(null));
        Assert.Equal(GamingModeKind.Manual, GamingPolicy.Parse("qualquer"));
        Assert.Equal("auto", GamingPolicy.Serialize(GamingModeKind.Automatic));
        Assert.Equal("manual", GamingPolicy.Serialize(GamingModeKind.Manual));

        // Automático aplicou; voltar ao Manual (o app chama Restore na troca) desfaz.
        var (sys, _, m) = Setup();
        m.Start("Counter-Strike 2", Scan(), GamingModeKind.Automatic, [LeaveSaver]);
        m.Restore();
        var manual = m.Start("Counter-Strike 2", Scan(), GamingModeKind.Manual, [LeaveSaver]);
        Assert.False(manual.Applied);
        Assert.Equal(PowerSchemes.PowerSaver, sys.ActiveScheme);
    }

    [Fact]
    public void Candidatos_mostram_o_estado_de_agora()
    {
        var candidates = GamingPolicy.Candidates(Scan(), [LeaveSaver]);
        var leave = Assert.Single(candidates, c => c.OptimizationId == LeaveSaver);
        Assert.True(leave.Authorized);
        Assert.True(leave.ApplicableNow);
        // PC já bem configurado: continua elegível, mas não há o que aplicar agora.
        var healthy = TestData.Engine().Evaluate(Pc.Healthy(), "gaming", "ultimate");
        Assert.Empty(GamingPolicy.ProposalsToApply(healthy, [LeaveSaver]));
    }

    [Fact]
    public void Limite_do_teste_gratis_vale_no_automatico()
    {
        var (sys, store, m) = Setup();
        var full = new TrialQuota(TrialQuota.MaxOptimizations, Enumerable.Range(0, TrialQuota.MaxOptimizations).Select(i => $"x{i}").ToHashSet());
        var r = m.Start("Counter-Strike 2", Scan(), GamingModeKind.Automatic, [LeaveSaver], full);
        Assert.False(r.Applied);
        Assert.Equal(PowerSchemes.PowerSaver, sys.ActiveScheme);
    }

    private static string StoreDir(SessionStore store) => Path.GetDirectoryName(Path.GetDirectoryName(store.LogPath)!)!;
}
