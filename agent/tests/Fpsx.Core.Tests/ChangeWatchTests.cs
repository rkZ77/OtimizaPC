using Fpsx.Core.Benchmark;
using Fpsx.Core.Engine;
using Fpsx.Core.Model;
using Fpsx.Core.Optimizations;

namespace Fpsx.Core.Tests;

/// <summary>Vigia do PC e resumo da semana: só avisam o que foi lido ou medido.</summary>
public class ChangeWatchTests
{
    private static (SessionStore Store, ScanResult Scan) Applied()
    {
        var sys = new FakeSystem();
        sys.Registry[(RegistryRoot.CurrentUser, RegistryPaths.GameBar.ToLowerInvariant(), "autogamemodeenabled")] = RegValue.DWord(0);
        sys.ActiveScheme = PowerSchemes.PowerSaver;
        sys.Displays[@"\.\DISPLAY1"] = new DisplayMode(1920, 1080, 60);
        var store = new SessionStore(TestData.TempDir());
        var scan = TestData.Engine().Evaluate(Pc.Misconfigured(), "gaming", "ultimate");
        var ids = scan.Optimizations.Where(o => o.AutoSelected).SelectMany(o => o.Evaluation.Proposals.Select(p => p.Id)).ToList();
        new OptimizationEngine(sys, store).Apply(scan, ids, new ApplyOptions());
        return (store, scan);
    }

    [Fact]
    public void Correcao_aplicada_que_voltou_ao_que_era_vira_aviso()
    {
        var (store, scan) = Applied();
        // A análise de agora vê o PC como antes das correções: algo desfez.
        var now = TestData.Engine().Evaluate(Pc.Misconfigured(), "gaming", "ultimate");
        var r = ChangeWatch.Compare(ChangeWatch.StateOf(scan), now, store.All(), _ => null);

        Assert.NotEmpty(r.Reverted);
        Assert.Contains("desfez", r.Title);
        Assert.Contains("corrigir de novo", r.Text);
    }

    [Fact]
    public void Pc_em_ordem_e_sem_mudanca_nao_avisa()
    {
        var healthy = TestData.Engine().Evaluate(Pc.Healthy(), "gaming", "ultimate");
        var r = ChangeWatch.Compare(ChangeWatch.StateOf(healthy), healthy, [], _ => null);
        Assert.False(r.HasNews);
    }

    [Fact]
    public void Primeira_analise_vira_base_sem_aviso_de_novo()
    {
        var now = TestData.Engine().Evaluate(Pc.Misconfigured(), "gaming", "ultimate");
        var r = ChangeWatch.Compare(null, now, [], _ => null);
        Assert.Empty(r.NewProblems);
        Assert.Null(r.WindowsChange);
    }

    [Fact]
    public void Windows_e_driver_novos_aparecem_e_nome_do_catalogo_e_usado()
    {
        var healthy = TestData.Engine().Evaluate(Pc.Healthy(), "gaming", "ultimate");
        var before = ChangeWatch.StateOf(healthy) with { WindowsBuild = 22631, GpuDriver = "31.0.15.1000" };
        var r = ChangeWatch.Compare(before, healthy, [], _ => null);

        Assert.Equal("Windows atualizado (versão 22631 para 26100)", r.WindowsChange);
        Assert.Contains("31.0.15.1000", r.DriverChange);
        // Nada desfeito nem novo: o aviso tranquiliza, sem inventar problema.
        Assert.Contains("continuam no lugar", r.Text);
    }

    [Fact]
    public void Problema_novo_aparece_uma_vez()
    {
        var healthy = TestData.Engine().Evaluate(Pc.Healthy(), "gaming", "ultimate");
        var bad = healthy with
        {
            Findings = [new Fpsx.Core.Diagnostics.Finding { DiagnosticId = "display", Status = HealthStatus.Problem, Title = "Monitor a 60 Hz" }],
        };
        var first = ChangeWatch.Compare(ChangeWatch.StateOf(healthy), bad, [], _ => null);
        Assert.NotEmpty(first.NewProblems);

        var second = ChangeWatch.Compare(ChangeWatch.StateOf(bad), bad, [], _ => null);
        Assert.Empty(second.NewProblems);
    }

    private static GameplaySession Match(string game, DateTimeOffset at, double fps) => new()
    {
        Id = Guid.NewGuid().ToString("N"), GameId = game, GameName = game.ToUpperInvariant(), StartedAt = at,
        MeasuredSeconds = 1800, Stats = new FrameStats { AvgFps = fps, Low1Fps = fps / 2 },
    };

    [Fact]
    public void Resumo_da_semana_so_com_partidas_suficientes()
    {
        var now = new DateTimeOffset(2026, 9, 29, 20, 0, 0, TimeSpan.Zero);
        Assert.Null(WeeklySummary.Build([Match("cs2", now.AddDays(-1), 100), Match("cs2", now.AddDays(-2), 100)], now));

        var all = new[] { 140.0, 142, 150 }.Select((f, i) => Match("cs2", now.AddDays(-1 - i), f))
            .Concat(new[] { 130.0, 136, 138 }.Select((f, i) => Match("cs2", now.AddDays(-8 - i), f)))
            .ToList();
        var s = WeeklySummary.Build(all, now)!.Value;
        Assert.Contains("3 partidas e 1,5 h", s.Text);
        Assert.Contains("CS2: FPS médio 142 e pior 1% 71", s.Text);
        Assert.Contains("na semana anterior, 136 e 68", s.Text);
    }
}
