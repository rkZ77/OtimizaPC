using Rkzfps.Core.Diagnostics;

namespace Rkzfps.Core.Tests;

public class DriverUpdatesTests
{
    [Fact]
    public void Resume_quantos_e_quais_sem_repetir_o_fabricante()
    {
        var drivers = new[]
        {
            new DriverUpdate("Realtek Semiconductor Corp. Driver Update (1030.52)", "Networking", "Realtek Semiconductor Corp.", null),
            new DriverUpdate("Audio Driver Update", "Media", "Contoso", null),
        };
        var s = DriverUpdates.Summary(drivers);
        Assert.StartsWith("2 drivers novos disponíveis no Windows Update:", s);
        Assert.Contains("Realtek Semiconductor Corp. Driver Update (1030.52)", s);
        Assert.DoesNotContain("Realtek Semiconductor Corp. Realtek", s);
        Assert.Contains("Contoso Audio Driver Update", s);
    }

    [Fact]
    public void Sem_driver_novo_diz_isso()
    {
        Assert.Equal("O Windows Update não tem driver novo para este PC agora.", DriverUpdates.Summary([]));
        var muitos = Enumerable.Range(1, 6).Select(i => new DriverUpdate($"Driver {i}", "", "", null)).ToList();
        Assert.EndsWith("e mais 2.", DriverUpdates.Summary(muitos));
    }
}
