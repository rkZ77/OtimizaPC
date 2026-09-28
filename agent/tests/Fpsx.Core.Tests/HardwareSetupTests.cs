using Fpsx.Core.Diagnostics;
using Fpsx.Core.Model;
using Fpsx.Core.Optimizations;

namespace Fpsx.Core.Tests;

public class HardwareSetupTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static IReadOnlyList<Finding> Run(SystemSnapshot s) => new HardwareSetupDiagnostic().Run(new EvaluationContext(s, [])).ToList();

    private static MemoryModule Stick(string part, int mts, string bank = "P0 CHANNEL A") =>
        new() { Slot = "DIMM 1", Bank = bank, CapacityBytes = 8 * Gb, ConfiguredMts = mts, PartNumber = part };

    [Theory]
    [InlineData("DDR4 3000", 3000)]            // caso real (PC do dono)
    [InlineData("CMK16GX4M2B3200C16", 3200)]   // Corsair
    [InlineData("F4-3600C18-8GVK", 3600)]      // G.Skill
    [InlineData("KF432C16BB/8", 3200)]         // Kingston Fury
    [InlineData("HX426C16FB3/8", 2666)]        // HyperX
    [InlineData("BL8G32C16U4B.M8FE1", 3200)]   // Crucial Ballistix
    [InlineData("M378A1K43EB2-CWE", null)]     // Samsung: código sem velocidade legível
    [InlineData("", null)]
    public void Le_a_velocidade_de_fabrica_do_codigo_do_pente(string part, int? expected)
    {
        Assert.Equal(expected, MemorySpeed.RatedFromPartNumber(part));
    }

    [Fact]
    public void Memoria_abaixo_da_velocidade_de_fabrica_explica_o_XMP_ou_EXPO()
    {
        // Caso real: 2 pentes DDR4 3000 rodando a 2666 num Ryzen.
        var s = Pc.Healthy() with
        {
            Cpu = Pc.Healthy().Cpu! with { Manufacturer = "AuthenticAMD" },
            Memory = Pc.Healthy().Memory! with { Modules = [Stick("DDR4 3000", 2666), Stick("DDR4 3000", 2666, "P0 CHANNEL B")] },
        };
        var f = Assert.Single(Run(s));
        Assert.Contains("2666 em vez de 3000", f.Title);
        Assert.Contains("EXPO", f.Recommendation);
        Assert.Equal(Potential.High, f.Impact);
    }

    [Fact]
    public void Memoria_na_velocidade_certa_ou_codigo_desconhecido_nao_gera_alerta()
    {
        var ok = Pc.Healthy() with { Memory = Pc.Healthy().Memory! with { Modules = [Stick("CMK16GX4M2B3200C16", 3200), Stick("CMK16GX4M2B3200C16", 3192, "B")] } };
        Assert.Empty(Run(ok));
        var unknown = Pc.Healthy() with { Memory = Pc.Healthy().Memory! with { Modules = [Stick("M378A1K43EB2-CWE", 2400), Stick("M378A1K43EB2-CWE", 2400, "B")] } };
        Assert.Empty(Run(unknown));
    }

    [Fact]
    public void Um_pente_so_pesa_mais_com_video_integrado()
    {
        var integrated = Pc.Healthy() with
        {
            Gpus = [new GpuInfo { Name = "AMD Radeon(TM) Graphics", Vendor = GpuVendor.Amd, LikelyIntegrated = true }],
            Memory = Pc.Healthy().Memory! with { Modules = [Stick("", 3200)] },
        };
        var f = Assert.Single(Run(integrated));
        Assert.Equal("Memória com um pente só", f.Title);
        Assert.Equal(Potential.High, f.Impact);
    }

    [Fact]
    public void Monitor_na_placa_mae_em_desktop_com_placa_de_video()
    {
        var s = Pc.Healthy() with
        {
            Gpus = [.. Pc.Healthy().Gpus, new GpuInfo { Name = "Intel(R) UHD Graphics 630", Vendor = GpuVendor.Intel, LikelyIntegrated = true }],
            Displays = [new DisplayInfo { DeviceName = @"\\.\DISPLAY1", IsPrimary = true, AdapterName = "Intel(R) UHD Graphics 630", CurrentHz = 60 }],
        };
        var f = Assert.Single(Run(s));
        Assert.Equal(HealthStatus.Problem, f.Status);
        Assert.Contains("placa de vídeo", f.Recommendation);

        // Notebook: passar pelo integrado é o normal (troca automática de placa).
        var laptop = s with { Power = s.Power! with { HasBattery = true } };
        Assert.Empty(Run(laptop));
        // Cabo na placa de vídeo: nada a dizer.
        var right = s with { Displays = [s.Displays[0] with { AdapterName = "NVIDIA GeForce RTX 3060" }] };
        Assert.Empty(Run(right));
    }

    [Fact]
    public void Jogo_no_HD_sugere_mover_para_o_SSD_com_espaco()
    {
        var s = Pc.Healthy() with
        {
            Disks = [.. Pc.Healthy().Disks, new DiskInfo { DriveLetter = "D:", Media = MediaKind.Hdd, TotalBytes = 1000 * Gb, FreeBytes = 300 * Gb }],
            Games = [new GameInstall { GameId = "cs2", Name = "Counter-Strike 2", InstallPath = @"D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive" }],
        };
        var f = Assert.Single(Run(s));
        Assert.Contains("HD comum (D:)", f.Title);
        Assert.Contains("SSD (C:", f.Recommendation);
        Assert.Contains("Mover pasta de instalação", f.Recommendation);
    }
}
