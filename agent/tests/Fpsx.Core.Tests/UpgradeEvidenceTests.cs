using Fpsx.Core.Benchmark;

namespace Fpsx.Core.Tests;

public class UpgradeEvidenceTests
{
    private static GameEvidence G(int drops = 0, int gpu = 0, int cpu = 0, double? avgCpu = null, double? avgGpu = null) =>
        new("cs2", "Counter-Strike 2", 4, 148, 87, 240, avgCpu, avgGpu, drops, gpu, cpu, 0);

    [Fact]
    public void Quedas_decidem_a_peca_que_segura_o_FPS()
    {
        Assert.Equal(Bottleneck.Gpu, UpgradeEvidence.Verdict([G(drops: 21, gpu: 17, cpu: 1)]));
        Assert.Equal(Bottleneck.Cpu, UpgradeEvidence.Verdict([G(drops: 10, gpu: 1, cpu: 6)]));
        Assert.Contains("17 de 21 quedas", UpgradeEvidence.VerdictText(Bottleneck.Gpu, [G(drops: 21, gpu: 17)]));
    }

    [Fact]
    public void Sem_quedas_suficientes_vale_o_uso_medio()
    {
        Assert.Equal(Bottleneck.Gpu, UpgradeEvidence.Verdict([G(drops: 2, gpu: 2, avgCpu: 40, avgGpu: 98)]));
        Assert.Equal(Bottleneck.Cpu, UpgradeEvidence.Verdict([G(avgCpu: 92, avgGpu: 60)]));
        Assert.Equal(Bottleneck.Balanced, UpgradeEvidence.Verdict([G(avgCpu: 50, avgGpu: 70)]));
    }

    [Fact]
    public void Sem_partida_medida_nao_afirma_nada()
    {
        Assert.Equal(Bottleneck.Unknown, UpgradeEvidence.Verdict([]));
        Assert.Contains("Jogue com o RKZFPS aberto", UpgradeEvidence.VerdictText(Bottleneck.Unknown, []));
    }
}
