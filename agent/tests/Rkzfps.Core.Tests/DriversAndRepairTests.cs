using Rkzfps.Core.Diagnostics;
using Rkzfps.Core.Engine;
using Rkzfps.Core.Model;

namespace Rkzfps.Core.Tests;

/// <summary>Driver pelo Windows Update e reparo dos arquivos do Windows.</summary>
public class DriversAndRepairTests
{
    private const string Gpu = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";
    private const string Audio = "7c9e6679-7425-40de-944b-e07fc1f90ae7";

    private static SystemSnapshot WithDrivers(params (string Id, string Title)[] drivers) => Pc.Healthy() with
    {
        PendingDrivers = drivers.Select(d => new DriverUpdate(d.Title, "Display", "AMD", new DateTime(2026, 9, 1)) { UpdateId = d.Id }).ToList(),
    };

    [Fact]
    public void Sem_busca_nao_existe_proposta_de_driver()
    {
        var scan = TestData.Engine().Evaluate(Pc.Healthy(), "gaming", "ultimate");
        Assert.Equal(Decision.NotApplicable, scan.Optimizations.Single(o => o.Definition.Id == "driver-update").Decision);
    }

    [Fact]
    public void Cada_driver_achado_vira_uma_proposta_que_nunca_e_automatica()
    {
        var scan = TestData.Engine().Evaluate(WithDrivers((Gpu, "AMD Radeon"), (Audio, "Realtek Audio")), "gaming", "ultimate");
        var r = scan.Optimizations.Single(o => o.Definition.Id == "driver-update");

        Assert.Equal(Decision.Optional, r.Decision);
        Assert.False(r.AutoSelected);
        Assert.Equal([$"driver-update:{Gpu}", $"driver-update:{Audio}"], r.Evaluation.Proposals.Select(p => p.Id));
        Assert.True(r.RequiresElevation);
    }

    [Fact]
    public void Free_ve_o_driver_mas_instalar_pede_plano()
    {
        var scan = TestData.Engine().Evaluate(WithDrivers((Gpu, "AMD Radeon")), "gaming", "free");
        Assert.Equal(Decision.Blocked, scan.Optimizations.Single(o => o.Definition.Id == "driver-update").Decision);
    }

    [Fact]
    public void Um_ponto_de_restauracao_por_sessao_antes_do_primeiro_driver()
    {
        var sys = new FakeSystem();
        var exec = new ChangeExecutor(sys);
        exec.Apply(new DriverInstallChange(Gpu, "AMD Radeon"));
        exec.Apply(new DriverInstallChange(Audio, "Realtek Audio"));

        Assert.Equal(["restore-point", $"driver {Gpu}", $"driver {Audio}"], sys.Log);
    }

    [Fact]
    public void Sem_protecao_do_sistema_instala_e_ensina_a_reverter()
    {
        var sys = new FakeSystem { RestorePointsEnabled = false };
        var detail = new ChangeExecutor(sys).Apply(new DriverInstallChange(Gpu, "AMD Radeon"));

        Assert.Contains($"driver {Gpu}", sys.Log);
        Assert.Contains("Reverter driver", detail);
    }

    [Theory]
    [InlineData(@"C:\drivers\nvlddmkm.inf")]
    [InlineData("https://site-qualquer/driver.exe")]
    [InlineData("")]
    public void Driver_so_pelo_id_do_windows_update(string id) =>
        Assert.Throws<SafetyViolationException>(() => SafetyPolicy.Validate(new DriverInstallChange(id, "Driver")));

    [Fact]
    public void Reparo_roda_dism_antes_do_sfc_e_pede_administrador()
    {
        var scan = TestData.Engine().Evaluate(Pc.Healthy(), "gaming", "ultimate");
        var r = scan.Optimizations.Single(o => o.Definition.Id == "system-files-repair");
        var changes = r.Evaluation.Proposals.Single().Changes.Cast<SystemRepairChange>().Select(c => c.Repair);

        Assert.Equal([SystemRepairKind.ImageRestoreHealth, SystemRepairKind.SystemFileScan], changes);
        Assert.Equal(Decision.Optional, r.Decision);
        Assert.True(r.RequiresElevation);
    }

    [Fact]
    public void Verificacao_le_o_estado_em_ingles_fixo_do_windows()
    {
        var ok = SystemHealthReport.Parse("IMG=Healthy\r\nDISK=NoErrorsFound\r\n");
        Assert.Equal(HealthStatus.Ok, ok.Status);

        var broken = SystemHealthReport.Parse("progresso qualquer\nIMG=Repairable\nDISK=NoErrorsFound");
        Assert.Equal(HealthStatus.Problem, broken.Status);
        Assert.Contains("dá para reparar", broken.Detail);

        var disk = SystemHealthReport.Parse("IMG=Healthy\nDISK=ScanNeeded");
        Assert.Equal(HealthStatus.Problem, disk.Status);
        Assert.Contains("chkdsk", disk.Detail);

        Assert.Equal(HealthStatus.Unknown, SystemHealthReport.Parse("").Status);
    }
}
