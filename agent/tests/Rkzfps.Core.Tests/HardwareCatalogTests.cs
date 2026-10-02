using Rkzfps.Core.Diagnostics;
using Rkzfps.Core.Hardware;
using Rkzfps.Core.Model;

namespace Rkzfps.Core.Tests;

public class HardwareCatalogTests
{
    private const long Gb = 1024L * 1024 * 1024;
    private static readonly HardwareCatalog Db = HardwareCatalog.Default;

    [Fact]
    public void O_arquivo_vem_junto_e_carrega()
    {
        Assert.NotEmpty(Db.Gpus);
        Assert.NotEmpty(Db.Cpus);
        Assert.All(Db.Gpus.Concat(Db.Cpus), m => Assert.NotEqual(HardwareTier.Unknown, m.Class));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 5060 Ti", "GeForce RTX 50", "dlss")]
    [InlineData("NVIDIA GeForce RTX 5060", "GeForce RTX 50", "reflex")]
    [InlineData("NVIDIA GeForce GTX 1650", "GeForce GTX 1650", "reflex")]
    [InlineData("AMD Radeon RX 5700 XT", "Radeon RX intermediária", "anti_lag")]
    [InlineData("AMD Radeon RX 7800 XT", "Radeon RX 7000/9000", "frame_gen")]
    [InlineData("Intel(R) Arc(TM) B580 Graphics", "Intel Arc", "xess")]
    public void Reconhece_o_modelo_pela_palavra_inteira(string name, string model, string feature)
    {
        var m = Db.FindGpu(name);
        Assert.NotNull(m);
        Assert.Equal(model, m!.Name);
        Assert.True(m.Has(feature));
    }

    [Fact]
    public void Trecho_menor_nao_engana()
    {
        // "rx 570" não pode casar com a RX 5700 XT, nem "rtx 5060" vencer a Ti.
        Assert.Equal(HardwareTier.Mid, Db.FindGpu("Radeon RX 5700 XT")!.Class);
        Assert.False(Db.FindGpu("GeForce GTX 1650")!.Has("dlss"));
        Assert.Null(Db.FindGpu("Placa desconhecida 9000"));
        Assert.Null(Db.FindGpu(null));
        Assert.True(Db.FindCpu("AMD Ryzen 7 5700X3D 8-Core Processor")!.Has(HardwareFeature.X3d));
        Assert.Null(Db.FindCpu("AMD Ryzen 5 5600 6-Core Processor"));
        Assert.Null(Db.FindCpu("Intel Core i5-12400F"));
    }

    private static SystemSnapshot Pc(string gpu, long vramGb, int threads = 16, double ramGb = 32) => Tests.Pc.Healthy() with
    {
        Cpu = Tests.Pc.Healthy().Cpu! with { Threads = threads },
        Gpus = [new GpuInfo { Name = gpu, Vendor = GpuVendor.Nvidia, VramBytes = vramGb * Gb }],
        Memory = new MemoryInfo { TotalBytes = (long)(ramGb * Gb), AvailableBytes = Gb, PagefilePresent = true },
    };

    [Fact]
    public void Nivel_do_PC_usa_o_modelo_quando_o_Windows_le_mal()
    {
        // WMI para em 4 GB: uma RTX 5060 Ti de 16 GB aparecia como 4 GB e o PC deixava de ser forte.
        Assert.Equal(HardwareTier.High, HardwareTierClassifier.Assess(Pc("NVIDIA GeForce RTX 5060 Ti", 4)).Tier);
        Assert.Equal(HardwareTier.Mid, HardwareTierClassifier.Assess(Pc("NVIDIA GeForce RTX 5060 Ti", 4), HardwareCatalog.Empty).Tier);
        // Placa de entrada com memória suficiente continua de entrada.
        var gtx = HardwareTierClassifier.Assess(Pc("NVIDIA GeForce GTX 1650", 4));
        Assert.Equal(HardwareTier.Low, gtx.Tier);
        Assert.Contains(gtx.Reasons, r => r.Contains("GTX 1650"));
        // Intermediária com 8 GB não vira forte.
        Assert.Equal(HardwareTier.Mid, HardwareTierClassifier.Assess(Pc("Radeon RX 580 Series", 8)).Tier);
        // Modelo desconhecido: igual a antes.
        Assert.Equal(HardwareTier.High, HardwareTierClassifier.Assess(Pc("GPU nova", 12)).Tier);
    }

    [Fact]
    public void Arquivo_ausente_ou_quebrado_nao_derruba()
    {
        var dir = TestData.TempDir();
        Assert.Same(HardwareCatalog.Empty, HardwareCatalog.LoadOrEmpty(dir));
        File.WriteAllText(Path.Combine(dir, "hardware.json"), "{ quebrado");
        Assert.Same(HardwareCatalog.Empty, HardwareCatalog.LoadOrEmpty(dir));
    }
}
