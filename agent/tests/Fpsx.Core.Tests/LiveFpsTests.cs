using System.Globalization;
using Fpsx.Core.Benchmark;

namespace Fpsx.Core.Tests;

public class LiveFpsTests
{
    private const string Header = "Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,Dropped,TimeInSeconds,msInPresentAPI,msBetweenPresents,AllowsTearing,PresentMode";

    private static string Row(double t, double ms, int pid = 42, string chain = "0xA") =>
        string.Create(CultureInfo.InvariantCulture, $"cs2.exe,{pid},{chain},DXGI,0,0,0,{t},0.1,{ms},1,Hardware: Independent Flip");

    [Fact]
    public void Mostra_o_fps_dos_ultimos_segundos()
    {
        var m = new LiveFpsMeter(42);
        Assert.Null(m.Current);
        m.Add(Header);
        // 5 s a 60 FPS, depois 3 s a 144 FPS: o "agora" é 144.
        for (var t = 0.0; t < 5; t += 1.0 / 60) m.Add(Row(t, 1000.0 / 60));
        for (var t = 5.0; t < 8; t += 1.0 / 144) m.Add(Row(t, 1000.0 / 144));
        Assert.Equal(144, m.Current!.Value, 0);
    }

    [Fact]
    public void Ignora_outro_processo_outra_cadeia_e_linha_quebrada()
    {
        var m = new LiveFpsMeter(42);
        m.Add(Header);
        for (var t = 0.0; t < 3; t += 0.01)
        {
            m.Add(Row(t, 10));                 // jogo, 100 FPS
            m.Add(Row(t, 1, pid: 99));          // outro processo
        }
        for (var t = 0.0; t < 3; t += 1) m.Add(Row(t, 1000, chain: "0xB")); // janela auxiliar a 1 FPS
        m.Add("cs2.exe,42,0xA,DXGI,0,0");        // linha cortada no meio da gravação
        Assert.Equal(100, m.Current!.Value, 0);
    }
}
