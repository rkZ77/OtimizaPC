using System.Text.Json;
using Fpsx.Core.Benchmark;
using Fpsx.Core.Json;

namespace Fpsx.Client.Tests;

public class GameplayUploadTests
{
    private static GameplaySession Match() => new()
    {
        Id = "20260927-201845-cs2",
        GameId = "cs2",
        GameName = "Counter-Strike 2",
        StartedAt = new DateTimeOffset(2026, 9, 27, 20, 18, 45, TimeSpan.FromHours(-3)),
        EndedAt = new DateTimeOffset(2026, 9, 27, 20, 50, 0, TimeSpan.FromHours(-3)),
        MeasuredSeconds = 1774,
        Stats = new FrameStats { AvgFps = 169.1, Low1Fps = 111.7, Low01Fps = 77.4, P99FrametimeMs = 9 },
        DisplayHz = 240,
        AppVersion = "0.4.2",
        Timeline = [new(100, 170, 150), new(102, 168, 20), new(104, 171, 160)],
        Load = [new(100, 60, 80, "Chrome", 30)],
        Hardware = new HardwareSummary { Tier = "MID", Gpu = "Radeon RX 580 Series", Threads = 8, RamGb = 15.9 },
    };

    [Fact]
    public void Sobe_numeros_grafico_e_hardware_mas_nao_o_nome_do_programa()
    {
        var up = GameplayUpload.From(Match());
        var json = JsonSerializer.Serialize(up, FpsxJson.Compact);

        Assert.Equal(1, up.Drops);
        Assert.Equal(1, up.DropCauses["app"]);
        Assert.Equal([102, 168, 20], up.Timeline[1]);
        Assert.Contains("\"session_key\":\"20260927-201845-cs2\"", json);
        Assert.Contains("\"gpu\":\"Radeon RX 580 Series\"", json);
        Assert.DoesNotContain("Chrome", json);
        Assert.DoesNotContain("Counter-Strike", json);
    }
}
