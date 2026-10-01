using Rkzfps.Core.Engine;
using Rkzfps.Core.Model;
using Rkzfps.Core.Optimizations;

namespace Rkzfps.Core.Tests;

/// <summary>Teste grátis: até 2 otimizações diferentes, garantido no motor.</summary>
public class TrialQuotaTests
{
    private static (FakeSystem Sys, SessionStore Store, OptimizationEngine Engine, ScanResult Scan) Setup()
    {
        var sys = new FakeSystem();
        sys.Registry[(RegistryRoot.CurrentUser, RegistryPaths.GameBar.ToLowerInvariant(), "autogamemodeenabled")] = RegValue.DWord(0);
        sys.ActiveScheme = PowerSchemes.PowerSaver;
        sys.Displays[@"\.\DISPLAY1"] = new DisplayMode(1920, 1080, 60);
        var store = new SessionStore(TestData.TempDir());
        var scan = TestData.Engine().Evaluate(Pc.Misconfigured(), "gaming", "pro");
        return (sys, store, new OptimizationEngine(sys, store), scan);
    }

    private static List<string> Auto(ScanResult scan) =>
        scan.Optimizations.Where(o => o.AutoSelected).SelectMany(o => o.Evaluation.Proposals.Select(p => p.Id)).ToList();

    private static int Distinct(SessionRecord s) =>
        s.Changes.Where(c => c.Status == ChangeStatus.Applied).Select(c => c.OptimizationId).Distinct().Count();

    [Fact]
    public void Teste_aplica_no_maximo_duas_otimizacoes_e_diz_o_motivo_das_outras()
    {
        var (_, store, engine, scan) = Setup();
        Assert.True(scan.Optimizations.Count(o => o.AutoSelected) > TrialQuota.MaxOptimizations);

        var session = engine.Apply(scan, Auto(scan), new ApplyOptions { Trial = TrialQuota.From(store.All()) });

        Assert.Equal(TrialQuota.MaxOptimizations, Distinct(session));
        Assert.Contains(session.Skipped, s => s.Reason == TrialQuota.SkipReason);
    }

    [Fact]
    public void Vaga_usada_nao_volta_com_desfazer()
    {
        var (sys, store, engine, scan) = Setup();
        var first = engine.Apply(scan, Auto(scan), new ApplyOptions { Trial = TrialQuota.From(store.All()) });
        new RollbackManager(sys, store).RollbackSession(first.Id, force: false);

        var quota = TrialQuota.From(store.All());
        Assert.Equal(0, quota.Remaining);

        // As mesmas duas podem voltar (ligar a chave de novo); uma terceira, não.
        var again = engine.Apply(scan, Auto(scan), new ApplyOptions { Trial = quota });
        Assert.Equal(TrialQuota.MaxOptimizations, Distinct(again));
        Assert.All(again.Changes.Select(c => c.OptimizationId), id => Assert.Contains(id, quota.Used));
    }

    [Fact]
    public void Plano_pago_nao_tem_limite()
    {
        var (_, _, engine, scan) = Setup();
        var session = engine.Apply(scan, Auto(scan), new ApplyOptions());
        Assert.True(Distinct(session) > TrialQuota.MaxOptimizations);
        Assert.DoesNotContain(session.Skipped, s => s.Reason == TrialQuota.SkipReason);
    }
}
