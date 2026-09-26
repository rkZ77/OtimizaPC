using System.Globalization;
using System.Text;
using Fpsx.Core.Benchmark;
using Fpsx.Core.Engine;

namespace Fpsx.Core.Tests;

public class GameplayTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 20, 0, 0, TimeSpan.FromHours(-3));

    /// <summary>CSV no formato real do PresentMon 2.5 com --v1_metrics (cabeçalho copiado de uma captura).</summary>
    private static string Csv(IEnumerable<(double T, double Ms, int Pid, string Chain)> rows)
    {
        var sb = new StringBuilder("Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,Dropped,TimeInSeconds,msInPresentAPI,msBetweenPresents,AllowsTearing,PresentMode,msUntilRenderComplete,msUntilDisplayed,msBetweenDisplayChange,msFlipDelay,msUntilRenderStart,msGPUActive\n");
        foreach (var (t, ms, pid, chain) in rows)
            sb.Append(CultureInfo.InvariantCulture, $"<unknown>,{pid},{chain},DXGI,0,512,0,{t:0.0000000},0.05,{ms:0.00000},1,Hardware: Independent Flip,0.1,0.2,0,0,0,0.1\n");
        return sb.ToString();
    }

    /// <summary>Partida sintética: quadros de ms constantes por tantos segundos.</summary>
    private static IEnumerable<(double, double, int, string)> Steady(double seconds, double ms, int pid = 42, string chain = "0xA", double start = 0)
    {
        for (var t = start; t < start + seconds; t += ms / 1000)
            yield return (t, ms, pid, chain);
    }

    private static List<bool> AllForeground(int seconds) => Enumerable.Repeat(true, seconds).ToList();

    private static List<double> Read(string csv, int pid, List<bool> fg)
    {
        using var reader = new StringReader(csv);
        return GameplayAnalyzer.Filter(PresentMonCsv.ReadTimedFrames(reader, pid), fg);
    }

    [Fact]
    public void Aquecimento_fica_de_fora_e_so_conta_o_PID_do_jogo()
    {
        // 60 s de carregamento a 30 ms, depois 4 min a 5 ms; outro processo a 1 ms no meio.
        var csv = Csv(Steady(60, 30).Concat(Steady(240, 5, start: 60)).Concat(Steady(300, 1, pid: 99)));
        var ft = Read(csv, 42, AllForeground(400));
        Assert.All(ft, ms => Assert.Equal(5, ms, 3));
        var stats = FrameStats.From(ft);
        Assert.Equal(200, stats.AvgFps, 0);
    }

    [Fact]
    public void Segundos_fora_de_primeiro_plano_nao_contam()
    {
        var csv = Csv(Steady(60, 5).Concat(Steady(120, 5, start: 60)).Concat(Steady(120, 50, start: 180)));
        // Alt-tab nos últimos 2 minutos: o jogo cai para 20 FPS em segundo plano.
        var fg = AllForeground(180).Concat(Enumerable.Repeat(false, 120)).ToList();
        Assert.All(Read(csv, 42, fg), ms => Assert.Equal(5, ms, 3));
    }

    [Fact]
    public void Tela_de_carregamento_nao_vira_travada()
    {
        var rows = Steady(200, 5).Append((150.0, 4000.0, 42, "0xA")).ToList();
        Assert.DoesNotContain(4000.0, Read(Csv(rows), 42, AllForeground(300)));
    }

    [Fact]
    public void Duas_cadeias_de_quadros_usa_a_do_jogo()
    {
        // Jogo a 144 FPS e uma janela auxiliar do mesmo processo a 1 FPS.
        var rows = Steady(300, 1000.0 / 144).Concat(Steady(300, 999, chain: "0xB")).OrderBy(r => r.Item1);
        var stats = FrameStats.From(Read(Csv(rows), 42, AllForeground(400)));
        Assert.Equal(144, stats.AvgFps, 0);
    }

    [Fact]
    public void Partida_curta_nao_e_registrada()
    {
        var ft = Read(Csv(Steady(150, 5)), 42, AllForeground(200)); // 90 s depois do aquecimento
        var (session, reason) = GameplayAnalyzer.Build(ft, "cs2", "Counter-Strike 2", T0, T0.AddMinutes(3), null, null, 144, "0.3.0");
        Assert.Null(session);
        Assert.Contains("mínimo", reason);
    }

    private static GameplaySession Match(DateTimeOffset at, double fps, string game = "cs2")
    {
        var ft = Enumerable.Repeat(1000.0 / fps, (int)(fps * 600)).ToList();
        return GameplayAnalyzer.Build(ft, game, "Counter-Strike 2", at, at.AddMinutes(10), null, null, 240, "0.3.0").Session!;
    }

    [Fact]
    public void Compara_partidas_antes_e_depois_da_otimizacao()
    {
        var pivot = T0.AddDays(1);
        var sessions = new[]
        {
            Match(T0, 150), Match(T0.AddHours(1), 152), Match(T0.AddHours(2), 149),
            Match(pivot.AddHours(1), 181), Match(pivot.AddHours(2), 178), Match(pivot.AddHours(3), 180),
            Match(pivot.AddHours(4), 90, game: "fortnite"),
        };
        var cmp = GameplayComparer.Compare(sessions, "cs2", "Counter-Strike 2", pivot, "otimização");
        Assert.True(cmp.Ready);
        Assert.Equal((3, 3), (cmp.BeforeCount, cmp.AfterCount));
        var avg = cmp.Result!.Metrics.Single(m => m.Metric == "FPS médio");
        Assert.True(avg.Improved);
        Assert.Contains(GameplayComparer.RealWorldWarning, cmp.Result.Warnings);
    }

    [Fact]
    public void Variacao_normal_entre_partidas_nao_vira_ganho()
    {
        var pivot = T0.AddDays(1);
        var sessions = new[] { Match(T0, 150), Match(T0.AddHours(1), 170), Match(pivot.AddHours(1), 155), Match(pivot.AddHours(2), 168) };
        var cmp = GameplayComparer.Compare(sessions, "cs2", "Counter-Strike 2", pivot, "otimização");
        Assert.DoesNotContain(cmp.Result!.Metrics, m => m.Improved || m.Worsened);
    }

    [Fact]
    public void Sem_partida_antes_explica_em_vez_de_comparar()
    {
        var cmp = GameplayComparer.Compare([Match(T0.AddDays(2), 180)], "cs2", "Counter-Strike 2", T0.AddDays(1), "otimização");
        Assert.False(cmp.Ready);
        Assert.Contains("ANTES", cmp.Status);
    }

    [Fact]
    public void FPS_preso_na_taxa_do_monitor_gera_aviso()
    {
        var pivot = T0.AddDays(1);
        var capped = new[] { Match(T0, 239) with { DisplayHz = 240 }, Match(pivot.AddHours(1), 239) with { DisplayHz = 240 } };
        var cmp = GameplayComparer.Compare(capped, "cs2", "Counter-Strike 2", pivot, "otimização");
        Assert.Contains(cmp.Result!.Warnings, w => w.Contains("taxa do monitor"));
    }

    [Fact]
    public void Marco_de_comparacao_ignora_sessao_desfeita_ou_sem_alteracao()
    {
        SessionRecord S(string id, SessionStatus status, ChangeStatus change, int day) => new()
        {
            Id = id, Status = status, StartedAt = T0.AddDays(day),
            Changes = [new ChangeRecord { Status = change, Applied = new Model.CacheClearChange(Model.CacheTarget.UserTemp) }],
        };
        var pivots = GameplayComparer.Pivots([S("a", SessionStatus.Completed, ChangeStatus.Applied, 1), S("b", SessionStatus.RolledBack, ChangeStatus.RolledBack, 2), S("c", SessionStatus.Completed, ChangeStatus.Failed, 3)]);
        Assert.Equal(["a"], pivots.Select(p => p.Id));
    }

    [Fact]
    public void Store_grava_e_le_as_partidas()
    {
        var store = new GameplayStore(TestData.TempDir());
        store.Save(Match(T0, 150));
        store.Save(Match(T0.AddHours(1), 160));
        File.WriteAllText(Path.Combine(store.Dir, "quebrado.json"), "{ nao e json");
        Assert.Equal([160, 150], store.All().Select(s => Math.Round(s.Stats.AvgFps)));
    }

    private static List<FpsPoint> Timeline(string csv, int pid, List<bool> fg)
    {
        using var reader = new StringReader(csv);
        return GameplayAnalyzer.Analyze(PresentMonCsv.ReadTimedFrames(reader, pid), fg).Timeline;
    }

    [Fact]
    public void Grafico_tem_um_ponto_por_segundo_com_media_e_pior_quadro()
    {
        // 2 min a 100 FPS com uma travada de 80 ms no segundo 90.
        var rows = Steady(90, 10).Append((90.0, 80.0, 42, "0xA")).Concat(Steady(90, 10, start: 90.08)).ToList();
        var tl = Timeline(Csv(rows), 42, AllForeground(200));

        Assert.Equal(60, tl[0].T);
        Assert.InRange(tl.Count, 118, 121);
        Assert.Equal(100, tl[0].Fps, 0);
        var spike = tl.Single(p => p.T == 90);
        Assert.Equal(12.5, spike.Low, 1);
        Assert.All(tl.Where(p => p.T != 90), p => Assert.Equal(100, p.Low, 0));
    }

    [Fact]
    public void Grafico_de_partida_longa_e_reduzido_sem_perder_a_travada()
    {
        // 3 h: o gráfico não passa do limite de pontos e o pior quadro sobrevive à redução.
        var rows = Steady(3 * 3600, 20).Append((5000.0, 250.0, 42, "0xA")).OrderBy(r => r.Item1).ToList();
        var tl = Timeline(Csv(rows), 42, AllForeground(3 * 3600 + 5));

        Assert.InRange(tl.Count, GameplayAnalyzer.MaxTimelinePoints - 5, GameplayAnalyzer.MaxTimelinePoints);
        Assert.Equal(4, tl.Min(p => p.Low), 1);
    }

    [Fact]
    public void Build_guarda_o_grafico_na_partida()
    {
        using var reader = new StringReader(Csv(Steady(300, 10)));
        var (ft, tl) = GameplayAnalyzer.Analyze(PresentMonCsv.ReadTimedFrames(reader, 42), AllForeground(300));
        var session = GameplayAnalyzer.Build(ft, "cs2", "Counter-Strike 2", T0, T0.AddMinutes(5), null, null, 144, "0.4.1", tl).Session!;
        Assert.Equal(tl.Count, session.Timeline.Count);
    }

    [Fact]
    public void Quedas_fortes_contam_uma_vez_por_queda()
    {
        var tl = new List<FpsPoint>
        {
            new(0, 100, 90), new(1, 100, 40), new(2, 100, 30), new(3, 100, 95), new(4, 100, 20), new(5, 100, 49.9),
        };
        Assert.Equal(2, GameplayAnalyzer.Drops(tl, 100));
        Assert.Equal(0, GameplayAnalyzer.Drops([new(0, 100, 60)], 100));
    }
}
