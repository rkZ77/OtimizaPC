using Rkzfps.Core.Diagnostics;
using Rkzfps.Core.Engine;
using Rkzfps.Core.Model;

namespace Rkzfps.Core.Tests;

/// <summary>Troca de peça ("preciso formatar?") e kit de drivers para formatar.</summary>
public class FormatKitTests
{
    private static readonly HardwareFingerprint Pc1 = new()
    {
        Board = "ASUS PRIME B550M", Cpu = "AMD Ryzen 5 5600", CpuVendor = "AuthenticAMD",
        Gpu = "NVIDIA GeForce RTX 3060", GpuVendor = "Nvidia", SystemDisk = "Kingston NV2", OtherDisks = [], RamGb = 16,
    };

    [Fact]
    public void Sem_base_nao_ha_troca()
    {
        Assert.Empty(HardwareAdvisor.Compare(null, Pc1));
        Assert.Empty(HardwareAdvisor.Compare(Pc1, Pc1));
    }

    [Fact]
    public void So_placa_mae_nova_pede_formatacao()
    {
        var board = HardwareAdvisor.Compare(Pc1, Pc1 with { Board = "MSI B650M", Cpu = "AMD Ryzen 5 7600" });
        var advice = Assert.Single(board);
        Assert.True(advice.FormatRecommended);
        Assert.Equal("Peça nova: formatar é o recomendado", HardwareAdvisor.Title(board));

        var cpu = Assert.Single(HardwareAdvisor.Compare(Pc1, Pc1 with { Cpu = "AMD Ryzen 7 5700X3D" }));
        Assert.False(cpu.FormatRecommended);
        Assert.Contains("BIOS", cpu.Steps);
    }

    [Fact]
    public void Placa_de_video_de_outra_marca_manda_remover_o_driver_antigo()
    {
        var other = Assert.Single(HardwareAdvisor.Compare(Pc1, Pc1 with { Gpu = "AMD Radeon RX 7600", GpuVendor = "Amd" }));
        Assert.False(other.FormatRecommended);
        Assert.Contains("Desinstale o programa do driver antigo", other.Steps);

        var same = Assert.Single(HardwareAdvisor.Compare(Pc1, Pc1 with { Gpu = "NVIDIA GeForce RTX 4060" }));
        Assert.Contains("Mesma marca", same.Steps);
    }

    [Fact]
    public void Disco_e_memoria_novos_nao_pedem_formatacao()
    {
        var r = HardwareAdvisor.Compare(Pc1, Pc1 with { OtherDisks = ["Seagate BarraCuda"], RamGb = 32 });
        Assert.Equal(["Disco novo", "Memória"], r.Select(a => a.Part));
        Assert.All(r, a => Assert.False(a.FormatRecommended));
    }

    private static readonly DriverKitManifest Kit = new()
    {
        Files = [new DriverKitFile(@"drivers\rt640x64\rt640x64.inf", "aa"), new DriverKitFile(@"drivers\rt640x64\rt640x64.sys", "bb")],
    };

    private static readonly string[] KitFiles = [@"drivers\rt640x64\rt640x64.inf", @"drivers\rt640x64\rt640x64.sys"];

    [Fact]
    public void Kit_intacto_passa()
    {
        var hashes = new Dictionary<string, string> { [KitFiles[0]] = "aa", [KitFiles[1]] = "BB" };
        Assert.Null(DriverKit.Verify(Kit, p => hashes.GetValueOrDefault(p), KitFiles));
    }

    [Fact]
    public void Kit_com_arquivo_trocado_a_mais_ou_faltando_e_recusado()
    {
        Assert.Contains("alterado", DriverKit.Verify(Kit, p => p.EndsWith(".sys") ? "cc" : "aa", KitFiles));
        Assert.Contains("não salvou", DriverKit.Verify(Kit, p => p.EndsWith(".sys") ? "bb" : "aa", [.. KitFiles, @"drivers\virus.inf"]));
        Assert.Contains("Falta", DriverKit.Verify(Kit, p => p.EndsWith(".sys") ? null : "aa", KitFiles));
        Assert.Contains("inválido", DriverKit.Verify(Kit with { Files = [new DriverKitFile(@"..\..\Windows\x.inf", "aa")] }, _ => "aa", []));
        Assert.NotNull(DriverKit.Verify(new DriverKitManifest(), _ => null, []));
    }

    [Theory]
    [InlineData(@"drivers\a.inf", true)]
    [InlineData(@"..\a.inf", false)]
    [InlineData(@"C:\Windows\a.inf", false)]
    [InlineData(@"drivers\\a.inf", false)]
    public void Caminho_do_kit_fica_dentro_da_pasta(string path, bool ok) => Assert.Equal(ok, DriverKit.SafeRelative(path));

    [Theory]
    [InlineData(@"relativo\kit")]
    [InlineData(@"E:\kit\..\..\Windows")]
    public void Pasta_do_kit_precisa_ser_absoluta_e_sem_subir(string folder) =>
        Assert.Throws<SafetyViolationException>(() => SafetyPolicy.Validate(new DriverPackageInstallChange(folder)));

    [Fact]
    public void Kit_so_vira_proposta_quando_a_pasta_foi_escolhida()
    {
        var none = TestData.Engine().Evaluate(Pc.Healthy(), "gaming", "ultimate");
        Assert.Equal(Decision.NotApplicable, none.Optimizations.Single(o => o.Definition.Id == "driver-kit-install").Decision);

        var chosen = TestData.Engine().Evaluate(Pc.Healthy() with { DriverKitFolder = @"E:\RKZFPS drivers PC 2026-09-29 1900" }, "gaming", "ultimate");
        var r = chosen.Optimizations.Single(o => o.Definition.Id == "driver-kit-install");
        Assert.Equal(Decision.Optional, r.Decision);
        Assert.True(r.RequiresElevation);
    }

    [Fact]
    public void Reinstalar_kit_cria_ponto_de_restauracao_antes()
    {
        var sys = new FakeSystem();
        new ChangeExecutor(sys).Apply(new DriverPackageInstallChange(@"E:\kit"));
        Assert.Equal(["restore-point", @"kit E:\kit"], sys.Log);
    }
}
