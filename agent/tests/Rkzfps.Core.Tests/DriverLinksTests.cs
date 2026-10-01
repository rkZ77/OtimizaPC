using Rkzfps.Core.Diagnostics;
using Rkzfps.Core.Model;

namespace Rkzfps.Core.Tests;

public class DriverLinksTests
{
    private static SystemSnapshot Pc_(GpuVendor gpu, string cpu, string boardMaker, string board) => Pc.Healthy() with
    {
        Gpus = [new GpuInfo { Name = "GPU", Vendor = gpu }],
        Cpu = Pc.Healthy().Cpu! with { Name = cpu, Manufacturer = "" },
        BoardManufacturer = boardMaker,
        BoardProduct = board,
    };

    [Fact]
    public void Ryzen_com_NVIDIA_e_Gigabyte_leva_a_quatro_fontes_oficiais()
    {
        var links = DriverLinks.For(Pc_(GpuVendor.Nvidia, "AMD Ryzen 3 3300X 4-Core Processor", "Gigabyte Technology Co., Ltd.", "B450M DS3H"));
        Assert.Equal(["Vídeo: NVIDIA App", "Chipset: AMD", "Placa-mãe: site do fabricante", "Drivers pelo Windows Update"], links.Select(l => l.Label));
        Assert.All(links, l => Assert.True(l.IsAllowed, l.Target));
        Assert.Contains("site%3Agigabyte.com", links[2].Target);
    }

    [Fact]
    public void Mesmo_site_para_video_e_chipset_nao_repete()
    {
        var links = DriverLinks.For(Pc_(GpuVendor.Amd, "AMD Ryzen 5 5600", "ASUSTeK COMPUTER INC.", "TUF GAMING B550M-PLUS"));
        Assert.Single(links, l => l.Target.Contains("amd.com"));
    }

    [Fact]
    public void Placa_sem_modelo_ou_de_marca_desconhecida_nao_ganha_busca_vaga()
    {
        Assert.Null(DriverLinks.BoardDriversUrl(Pc_(GpuVendor.Nvidia, "Intel", "Gigabyte", "Default string")));
        Assert.Null(DriverLinks.BoardDriversUrl(Pc_(GpuVendor.Nvidia, "Intel", "Fabricante Qualquer", "X99")));
        // Windows Update aparece sempre, mesmo sem nada identificado.
        Assert.Contains(DriverLinks.For(Pc_(GpuVendor.Unknown, "", "", "")), l => l == FindingAction.WindowsUpdateDrivers);
    }

    [Fact]
    public void Prazo_do_driver_de_video_e_menor_em_placa_dedicada_NVIDIA_e_AMD()
    {
        Assert.Equal(183, GpuDriverDiagnostic.MaxDriverAgeDays(new GpuInfo { Vendor = GpuVendor.Nvidia }));
        Assert.Equal(183, GpuDriverDiagnostic.MaxDriverAgeDays(new GpuInfo { Vendor = GpuVendor.Amd }));
        Assert.Equal(365, GpuDriverDiagnostic.MaxDriverAgeDays(new GpuInfo { Vendor = GpuVendor.Amd, LikelyIntegrated = true }));
        Assert.Equal(365, GpuDriverDiagnostic.MaxDriverAgeDays(new GpuInfo { Vendor = GpuVendor.Intel }));
    }
}
