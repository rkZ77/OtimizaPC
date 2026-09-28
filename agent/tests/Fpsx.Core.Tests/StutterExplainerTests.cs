using Fpsx.Core.Benchmark;

namespace Fpsx.Core.Tests;

public class StutterExplainerTests
{
    private static List<FpsPoint> Steady(params int[] dropsAt) =>
        Enumerable.Range(0, 300).Select(t => new FpsPoint(t, 160, dropsAt.Contains(t) ? 20 : 120)).ToList();

    [Fact]
    public void Queda_junto_com_programa_pesado_aponta_o_programa()
    {
        var load = new List<LoadSample> { new(100, 60, 70, "Chrome", 35), new(200, 50, 60, "Discord", 2) };
        var causes = StutterExplainer.Explain(Steady(101, 202), load, 160);

        Assert.Equal(2, causes.Count);
        Assert.Equal(DropCauseKind.App, causes[0].Kind);
        Assert.Contains("Chrome usando 35%", causes[0].Text);
        // Discord a 2% não é suspeito: com o PC tranquilo, a queda é do jogo.
        Assert.Equal(DropCauseKind.Game, causes[1].Kind);
    }

    [Fact]
    public void Cpu_ou_gpu_no_limite_e_sem_amostra_perto()
    {
        var load = new List<LoadSample> { new(50, 96, 70, null, 0), new(150, 60, 99, null, 0) };
        var causes = StutterExplainer.Explain(Steady(52, 150, 280), load, 160);

        Assert.Equal([DropCauseKind.Cpu, DropCauseKind.Gpu, DropCauseKind.Unknown], causes.Select(c => c.Kind));
    }

    [Fact]
    public void Resumo_culpa_o_programa_so_quando_ele_aparece_bastante()
    {
        var chrome = new DropCause(1, DropCauseKind.App, "", "Chrome");
        var game = new DropCause(2, DropCauseKind.Game, "", null);
        Assert.StartsWith("2 de 3 quedas coincidiram com o Chrome", StutterExplainer.Summary([chrome, chrome with { T = 5 }, game]));
        Assert.StartsWith("4 de 5 quedas aconteceram com o PC tranquilo", StutterExplainer.Summary([chrome, game, game, game, game]));
        Assert.Equal("", StutterExplainer.Summary([]));
    }

    [Fact]
    public void Resumo_do_hardware_nao_leva_nome_do_pc()
    {
        var hw = HardwareSummary.From(Pc.Healthy());
        Assert.Equal("NVIDIA GeForce RTX 3060", hw.Gpu);
        Assert.Equal(12, hw.Threads);
        Assert.Equal("HIGH", hw.Tier);
        Assert.DoesNotContain(typeof(HardwareSummary).GetProperties(), p => p.Name.Contains("Name") || p.Name.Contains("User"));
    }
}
