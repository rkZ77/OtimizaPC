using Fpsx.Core.Benchmark;
using Fpsx.Core.Model;

namespace Fpsx.Core.Tests;

public class GpuSelectionTests
{
    // Chaves de propósito fora de ordem: a lógica não pode supor "GPU 0 é a integrada".
    private static readonly GpuAdapter Rtx = new("00000000000A0001", "NVIDIA GeForce RTX 4060 Laptop GPU", GpuRole.Dedicated, 8188, 0);
    private static readonly GpuAdapter Iris = new("00000000000B0002", "Intel(R) Iris(R) Xe Graphics", GpuRole.Integrated, 128, 1);
    private static readonly GpuAdapter Basic = new("00000000000C0003", "Microsoft Basic Render Driver", GpuRole.Virtual, 0, 2);

    private static Dictionary<string, double> Load(params (GpuAdapter A, double Sum)[] l) => l.ToDictionary(x => x.A.Luid, x => x.Sum);

    [Fact]
    public void Pc_com_uma_gpu_nunca_alerta()
    {
        var r = GpuSelector.Evaluate([Rtx, Basic], Load((Rtx, 5000)), 60);
        Assert.Equal(GpuSelectionStatus.SingleGpu, r.Status);
        // Mesmo sem nenhuma leitura do jogo: uma placa só não tem o que escolher.
        Assert.Equal(GpuSelectionStatus.SingleGpu, GpuSelector.Evaluate([Rtx], new Dictionary<string, double>(), 0).Status);
        Assert.Equal(GpuSelectionStatus.SingleGpu, GpuSelector.Evaluate([], new Dictionary<string, double>(), 0).Status);
    }

    [Fact]
    public void Igpu_mais_dedicada_escolhe_a_dedicada_como_alto_desempenho()
    {
        Assert.Equal(Rtx, GpuSelector.HighPerformance([Iris, Rtx, Basic]));
        // Mesmo que o Windows liste a integrada primeiro, a dedicada presente é a de alto desempenho.
        Assert.Equal(Rtx.Luid, GpuSelector.HighPerformance([Iris with { PreferenceOrder = 0 }, Rtx with { PreferenceOrder = 1 }])?.Luid);
    }

    [Fact]
    public void Jogo_na_dedicada_esta_correto()
    {
        var r = GpuSelector.Evaluate([Iris, Rtx], Load((Rtx, 5800), (Iris, 200)), 60);
        Assert.Equal(GpuSelectionStatus.Correct, r.Status);
        Assert.Equal(Rtx.Name, r.UsedGpu);
        Assert.Contains("alto desempenho", r.Text);
    }

    [Fact]
    public void Jogo_na_igpu_com_dedicada_disponivel_alerta()
    {
        var r = GpuSelector.Evaluate([Rtx, Iris], Load((Iris, 5400)), 60);
        Assert.Equal(GpuSelectionStatus.Wrong, r.Status);
        Assert.Equal(Iris.Name, r.UsedGpu);
        Assert.Equal(Rtx.Name, r.HighPerformanceGpu);
        Assert.Equal(90, r.UsedGpuLoad);
        Assert.Contains($"O jogo está utilizando a {Iris.Name}", r.Text);
        Assert.Contains($"enquanto a {Rtx.Name}", r.Text);
    }

    [Fact]
    public void Igpu_como_unica_placa_fisica_nao_e_errada()
    {
        // Notebook sem dedicada: a integrada é a única opção.
        Assert.Equal(GpuSelectionStatus.SingleGpu, GpuSelector.Evaluate([Iris, Basic], Load((Iris, 5000)), 60).Status);
    }

    [Fact]
    public void Gpu_nao_identificavel_nao_inventa()
    {
        // Uso dividido entre as placas: nenhuma concentra 80%.
        var split = GpuSelector.Evaluate([Iris, Rtx], Load((Iris, 3000), (Rtx, 3000)), 60);
        Assert.Equal(GpuSelectionStatus.Undetermined, split.Status);
        Assert.StartsWith("Não foi possível determinar", split.Text);
        // A placa do jogo não está na lista do DXGI (adaptador sumiu, driver trocado).
        var unknown = GpuSelector.Evaluate([Iris, Rtx], new Dictionary<string, double> { ["FFFFFFFF00000000"] = 5000 }, 60);
        Assert.Equal(GpuSelectionStatus.Undetermined, unknown.Status);
        Assert.Equal("Não foi possível determinar", GpuSelector.Label(unknown.Status));
    }

    [Fact]
    public void Jogo_sem_perfil_usa_a_mesma_regra()
    {
        var s = new GameplaySession { GameId = "exe-eldenring", Detected = true, Gpu = GpuSelector.Evaluate([Rtx, Iris], Load((Iris, 5000)), 60) };
        Assert.Equal(GpuSelectionStatus.Wrong, s.Gpu!.Status);
    }

    [Fact]
    public void Multiplas_dedicadas_nao_afirmam_perda()
    {
        var gtx = new GpuAdapter("00000000000D0004", "NVIDIA GeForce GTX 1650", GpuRole.Dedicated, 4096, 1);
        var r = GpuSelector.Evaluate([Rtx, gtx, Iris], Load((gtx, 5000)), 60);
        Assert.Equal(GpuSelectionStatus.OtherDedicated, r.Status);
        Assert.Contains("não dá para afirmar", r.Text);
        // Jogo na melhor das três: correto.
        Assert.Equal(GpuSelectionStatus.Correct, GpuSelector.Evaluate([gtx, Iris, Rtx], Load((Rtx, 5000)), 60).Status);
    }

    [Fact]
    public void Jogo_fechado_durante_a_leitura_fica_indeterminado()
    {
        // Só 3 leituras com uso de GPU antes de o processo sumir.
        var r = GpuSelector.Evaluate([Rtx, Iris], Load((Iris, 270)), 3);
        Assert.Equal(GpuSelectionStatus.Undetermined, r.Status);
        Assert.Contains("tempo suficiente", r.Text);
    }

    [Fact]
    public void Driver_sem_informacao_suficiente()
    {
        // Placa sem memória informada e sem marcação híbrida: papel desconhecido.
        Assert.Equal(GpuRole.Unknown, GpuSelector.Classify(false, false, null, null, 0, false));
        var mystery = new GpuAdapter("00000000000E0005", "Placa X", GpuRole.Unknown, 0, 0);
        var r = GpuSelector.Evaluate([mystery, Iris], Load((mystery, 5000)), 60);
        Assert.Equal(GpuSelectionStatus.Undetermined, r.Status);
        Assert.Contains("driver não informou", r.Text);
        // Sem nenhuma dedicada ou lista do Windows, também não há "alto desempenho" para comparar.
        var noBest = GpuSelector.Evaluate([Iris with { PreferenceOrder = null }, mystery with { PreferenceOrder = null }], Load((Iris, 5000)), 60);
        Assert.Equal(GpuSelectionStatus.Undetermined, noBest.Status);
    }

    [Theory]
    // software, remoto/virtual, híbrida integrada, híbrida dedicada, MB dedicados, nome de integrada -> papel
    [InlineData(true, false, null, null, 0L, false, GpuRole.Virtual)]
    [InlineData(false, true, null, null, 8192L, false, GpuRole.Virtual)]
    [InlineData(false, false, true, null, 8192L, false, GpuRole.Integrated)]
    [InlineData(false, false, null, true, 512L, false, GpuRole.Dedicated)]
    [InlineData(false, false, null, null, 128L, false, GpuRole.Integrated)]
    [InlineData(false, false, null, null, 8192L, false, GpuRole.Dedicated)]
    // APU com 2 GB reservados na BIOS: o nome desempata.
    [InlineData(false, false, null, null, 2048L, true, GpuRole.Integrated)]
    public void Papel_da_placa_vem_do_driver_antes_do_nome(bool software, bool remote, bool? hi, bool? hd, long mb, bool nameIntegrated, GpuRole expected) =>
        Assert.Equal(expected, GpuSelector.Classify(software, remote, hi, hd, mb, nameIntegrated));

    [Fact]
    public void Nome_so_desempata()
    {
        Assert.True(GpuSelector.NameLooksIntegrated(GpuVendor.Amd, "AMD Radeon(TM) Graphics"));
        Assert.False(GpuSelector.NameLooksIntegrated(GpuVendor.Amd, "Radeon RX 580 Series"));
        Assert.False(GpuSelector.NameLooksIntegrated(GpuVendor.Intel, "Intel(R) Arc(TM) A770 Graphics"));
    }
}

public class GpuSelectionDiagnosisTests
{
    private static GameplaySession Match(GpuSelection? gpu) => new()
    {
        Stats = FrameStats.From(Enumerable.Range(0, 60_000).Select(i => i % 50 == 0 ? 14.0 : 9.9).ToList()),
        DisplayHz = 144,
        Timeline = Enumerable.Range(0, 600).Select(t => new FpsPoint(t, 80 + t % 40, 60)).ToList(),
        // Placa no limite e núcleos folgados: sem a informação da GPU errada, daria GPU LIMITANDO.
        Load = Enumerable.Range(1, 120).Select(i => new LoadSample(i * 5, 40, 99, null, 0) { Foreground = true, CpuMaxCore = 60 }).ToList(),
        Gpu = gpu,
    };

    [Fact]
    public void Gpu_errada_vira_configuracao_e_nunca_gpu_limitando()
    {
        var wrong = new GpuSelection { Status = GpuSelectionStatus.Wrong, UsedGpu = "Intel Iris Xe", HighPerformanceGpu = "RTX 4060", Text = "O jogo está utilizando a Intel Iris Xe enquanto a RTX 4060 está disponível." };
        var d = LimitAnalyzer.Diagnose(Match(wrong));
        Assert.Equal(LimitKind.Software, d.Kind);
        Assert.Contains("Intel Iris Xe", d.Why);
        Assert.Contains(d.Evidence, e => e.Kind == EvidenceKind.Measured && e.Text == wrong.Text);
    }

    [Theory]
    [InlineData(GpuSelectionStatus.Correct)]
    [InlineData(GpuSelectionStatus.SingleGpu)]
    [InlineData(GpuSelectionStatus.OtherDedicated)]
    [InlineData(GpuSelectionStatus.Undetermined)]
    public void Outros_status_nao_mudam_o_diagnostico(GpuSelectionStatus status)
    {
        Assert.Equal(LimitKind.Gpu, LimitAnalyzer.Diagnose(Match(new GpuSelection { Status = status, Text = "x" })).Kind);
        Assert.Equal(LimitKind.Gpu, LimitAnalyzer.Diagnose(Match(null)).Kind);
    }

    [Fact]
    public void Status_indeterminado_aparece_como_nao_disponivel()
    {
        var d = LimitAnalyzer.Diagnose(Match(new GpuSelection { Status = GpuSelectionStatus.Undetermined, Text = "Não foi possível determinar a placa." }));
        Assert.Contains(d.Evidence, e => e.Kind == EvidenceKind.Unavailable && e.Text.StartsWith("Não foi possível determinar"));
    }
}
