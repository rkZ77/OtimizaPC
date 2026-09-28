using Fpsx.Core.Benchmark;

namespace Fpsx.Core.Tests;

public class DisplayAdviceTests
{
    [Fact]
    public void Fps_bem_abaixo_do_monitor_vira_alerta_com_o_que_fazer()
    {
        // Caso real: monitor de 240 Hz, CS2 a 161 FPS de média e 1% low 77.
        // O limite sugerido vem do que o PC segura em 90% dos trechos (~140),
        // não do 1% low (limitar em 70 jogaria fora metade do FPS).
        var tl = Enumerable.Range(0, 100).Select(t => new FpsPoint(t, t < 10 ? 120 : 145 + t % 30, 90)).ToList();
        var r = DisplayAdvice.For(161, 77, 240, tl)!.Value;
        Assert.True(r.Alert);
        Assert.Contains("240 Hz", r.Text);
        Assert.Contains("FreeSync ou G-Sync", r.Text);
        Assert.Contains("perto de 140", r.Text);
        Assert.DoesNotContain("perto de 70", r.Text);
        // Sem gráfico, não chuta número.
        Assert.DoesNotContain("Limitar o FPS", DisplayAdvice.For(161, 77, 240)!.Value.Text);
    }

    [Fact]
    public void Fps_colado_na_taxa_nao_e_alerta_e_perto_dela_nao_diz_nada()
    {
        var capped = DisplayAdvice.For(143, 120, 144)!.Value;
        Assert.False(capped.Alert);
        Assert.Contains("não foi o limite", capped.Text);
        Assert.Null(DisplayAdvice.For(130, 100, 144));
        Assert.Null(DisplayAdvice.For(130, 100, null));
    }

    [Fact]
    public void Sem_dica_de_limite_quando_o_1_low_e_baixo_demais()
    {
        var r = DisplayAdvice.For(45, 22, 144, Enumerable.Range(0, 60).Select(t => new FpsPoint(t, 25, 15)).ToList())!.Value;
        Assert.True(r.Alert);
        Assert.DoesNotContain("Limitar o FPS", r.Text);
    }
}
