using Fpsx.Core.Benchmark;
using Fpsx.Core.Diagnostics;
using Fpsx.Core.Games;
using Fpsx.Core.Model;
using Fpsx.Core.Reporting;

namespace Fpsx.Core.Tests;

public class DiagnosticsTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static IReadOnlyList<Finding> Run(SystemSnapshot s) => TestData.Engine().Evaluate(s, "gaming", "ultimate").Findings;

    [Fact]
    public void PC_saudavel_nao_gera_problema()
    {
        Assert.DoesNotContain(Run(Pc.Healthy()), f => f.Status is HealthStatus.Problem or HealthStatus.Attention);
    }

    [Fact]
    public void Pressao_de_memoria_aponta_os_maiores_consumidores()
    {
        var s = Pc.Healthy() with
        {
            Memory = new MemoryInfo { TotalBytes = 16 * Gb, AvailableBytes = Gb, PagefilePresent = true },
            Processes = [new ProcessSample { Pid = 1, Name = "chrome", WorkingSetBytes = 6 * Gb }],
        };
        var f = Assert.Single(Run(s), f => f.DiagnosticId == "memory-pressure" && f.Status == HealthStatus.Problem);
        Assert.Contains("chrome", f.Recommendation);
    }

    [Fact]
    public void Pagefile_desligado_com_pouca_RAM_gera_atencao()
    {
        var s = Pc.Healthy() with { Memory = new MemoryInfo { TotalBytes = 8 * Gb, AvailableBytes = 5 * Gb, PagefilePresent = false } };
        Assert.Contains(Run(s), f => f.Title == "Arquivo de paginação desativado");
    }

    [Fact]
    public void Throttling_sob_carga_e_problema_fisico_nao_tweak()
    {
        var s = Pc.Healthy() with { Cpu = Pc.Healthy().Cpu! with { SampledUnderLoad = true, AvgPerformanceLimitPercent = 80, MinPerformanceLimitPercent = 62 } };
        var f = Assert.Single(Run(s), f => f.DiagnosticId == "cpu-throttling");
        Assert.Equal(HealthStatus.Problem, f.Status);
        Assert.Contains("refrigeração", f.Recommendation);
    }

    [Fact]
    public void Jogo_em_HD_fala_de_carregamento_e_nao_de_FPS()
    {
        var s = Pc.WithCs2(Pc.Healthy() with { Disks = [Pc.Healthy().Disks[0] with { Media = MediaKind.Hdd }] }, new() { ["setting.mat_vsync"] = "0" });
        var f = Assert.Single(Run(s), f => f.DiagnosticId == "storage-health" && f.Status == HealthStatus.Attention);
        Assert.Equal("LOADING", f.ImpactArea);
        Assert.Contains("não reduz o FPS médio", f.Detail);
    }

    [Fact]
    public void Driver_com_mais_de_um_ano_sugere_site_oficial()
    {
        var s = Pc.Healthy() with { Gpus = [Pc.Healthy().Gpus[0] with { DriverDate = new DateTime(2024, 1, 1) }] };
        var f = Assert.Single(Run(s), f => f.Area == Areas.Driver);
        Assert.Equal(HealthStatus.Attention, f.Status);
        Assert.Contains("nvidia.com", f.Recommendation);
    }

    [Fact]
    public void Perda_no_gateway_e_rede_local_perda_depois_e_provedor()
    {
        var local = Pc.Healthy() with
        {
            Network = Pc.Healthy().Network! with
            {
                AdapterType = "WIFI",
                Pings = [new PingResult { Target = "192.168.0.1", Role = "gateway", Sent = 20, Received = 17, AvgMs = 4, JitterMs = 2 }],
            },
        };
        var f1 = Assert.Single(Run(local), f => f.DiagnosticId == "network-quality" && f.Status == HealthStatus.Problem);
        Assert.Contains("roteador", f1.Detail);

        var isp = Pc.Healthy() with
        {
            Network = Pc.Healthy().Network! with
            {
                Pings =
                [
                    new PingResult { Target = "192.168.0.1", Role = "gateway", Sent = 20, Received = 20, AvgMs = 1, JitterMs = 0.1 },
                    new PingResult { Target = "1.1.1.1", Role = "internet", Sent = 20, Received = 18, AvgMs = 20, JitterMs = 2 },
                ],
            },
        };
        var f2 = Assert.Single(Run(isp), f => f.DiagnosticId == "network-quality" && f.Status == HealthStatus.Problem);
        Assert.Contains("provedor", f2.Recommendation);
        Assert.Contains("DNS não resolve", f2.Recommendation);
    }

    [Fact]
    public void Integridade_de_memoria_e_so_informativa()
    {
        var f = Assert.Single(Run(Pc.Healthy()), f => f.DiagnosticId == "memory-integrity-status");
        Assert.Equal(HealthStatus.Info, f.Status);
        Assert.Contains("não desativa", f.Detail);
    }

    [Fact]
    public void CS2_com_vsync_e_60Hz_em_monitor_144_gera_as_duas_recomendacoes()
    {
        var s = Pc.WithCs2(Pc.Healthy(), new()
        {
            ["setting.mat_vsync"] = "1",
            ["setting.r_low_latency"] = "0",
            ["setting.refreshrate_numerator"] = "60",
            ["setting.refreshrate_denominator"] = "1",
        });
        var game = Run(s).Where(f => f.DiagnosticId == "game-settings").ToList();
        Assert.Contains(game, f => f.Title.Contains("V-Sync") && f.Status == HealthStatus.Attention);
        Assert.Contains(game, f => f.Title.Contains("Reflex"));
        Assert.Contains(game, f => f.Title.Contains("taxa de atualização") && f.Detail.Contains("144"));
    }

    [Fact]
    public void Reflex_nao_e_sugerido_sem_GPU_NVIDIA()
    {
        var s = Pc.WithCs2(Pc.Healthy() with { Gpus = [new GpuInfo { Name = "AMD Radeon RX 7800 XT", Vendor = GpuVendor.Amd, DriverDate = new DateTime(2026, 6, 1) }] },
            new() { ["setting.r_low_latency"] = "0", ["setting.mat_vsync"] = "0" });
        Assert.DoesNotContain(Run(s), f => f.Title.Contains("Reflex"));
    }

    [Fact]
    public void Relatorio_conta_sem_inventar_nota()
    {
        var report = ReportBuilder.Build(TestData.Engine().Evaluate(Pc.Misconfigured(), "gaming", "ultimate"));
        Assert.True(report.Recommended >= 3);
        Assert.Contains(report.Readiness, r => r.Area == Areas.GameMode && r.Status == HealthStatus.Attention);
        Assert.Contains(report.Readiness, r => r.Area == Areas.Display && r.Status == HealthStatus.Attention);
        Assert.Equal("NVIDIA GeForce RTX 3060", report.Hardware["GPU"]);
    }
}

public class ValveFilesTests
{
    [Fact]
    public void Le_cs2_video_txt()
    {
        const string text = """
            "video.cfg"
            {
            	"Version"		"15"
            	"setting.defaultres"		"1920"
            	"setting.mat_vsync"		"0"
            	"setting.refreshrate_numerator"		"144000"
            	"setting.refreshrate_denominator"		"1000"
            }
            """;
        var kv = ValveFiles.ParseFlatKeyValues(text);
        Assert.Equal("1920", kv["setting.defaultres"]);
        Assert.Equal("144000", kv["SETTING.REFRESHRATE_NUMERATOR"]);
    }

    [Fact]
    public void Le_bibliotecas_da_steam()
    {
        const string vdf = """
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"C:\\Program Files (x86)\\Steam"
            		"apps" { "228980" "0" }
            	}
            	"1"
            	{
            		"path"		"D:\\SteamLibrary"
            	}
            }
            """;
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], ValveFiles.ParseLibraryFolders(vdf));
    }
}

public class BenchmarkTests
{
    [Fact]
    public void Estatisticas_seguem_as_definicoes_documentadas()
    {
        // 990 quadros de 5 ms e 10 de 20 ms: exatamente 1% de quadros lentos.
        var frames = Enumerable.Repeat(5.0, 990).Concat(Enumerable.Repeat(20.0, 10)).ToList();
        var s = FrameStats.From(frames);

        Assert.Equal(1000, s.Frames);
        Assert.Equal(5.15, s.DurationSeconds, 3);
        Assert.Equal(1000 / 5.15, s.AvgFps, 3);
        Assert.Equal(200, s.Low1Fps, 3); // P99 cai no último quadro de 5 ms
        Assert.Equal(50, s.Low01Fps, 3); // P99.9 cai nos de 20 ms
        Assert.Equal(10, s.StutterCount);
        Assert.True(s.Sufficient);
    }

    [Fact]
    public void Captura_curta_e_marcada_como_insuficiente()
    {
        Assert.False(FrameStats.From(Enumerable.Repeat(7.0, 200).ToList()).Sufficient);
        Assert.Equal(0, FrameStats.From([]).Frames);
    }

    [Fact]
    public void Le_CSV_do_PresentMon_1_e_2_filtrando_pelo_processo()
    {
        const string v1 = "Application,ProcessID,MsBetweenPresents\ncs2.exe,1,6.5\nDiscord.exe,2,16.6\ncs2.exe,1,7.5\n";
        Assert.Equal([6.5, 7.5], PresentMonCsv.ReadFrametimes(v1, "cs2.exe"));

        const string v2 = "Application,ProcessID,PresentMode,FrameTime,CPUBusy\ncs2.exe,1,\"Hardware: Independent Flip\",6.9,3\n";
        Assert.Equal([6.9], PresentMonCsv.ReadFrametimes(v2, "cs2.exe"));

        Assert.Throws<InvalidDataException>(() => PresentMonCsv.ReadFrametimes("a,b\n1,2\n"));
    }

    private static FrameStats Run(double avgFps, double low1) =>
        new() { Frames = 10000, DurationSeconds = 60, AvgFps = avgFps, Low1Fps = low1, Low01Fps = low1 * 0.8, AvgFrametimeMs = 1000 / avgFps };

    [Fact]
    public void Diferenca_pequena_numa_rodada_nao_vira_ganho()
    {
        var cmp = BenchmarkComparer.Compare([Run(142, 91)], [Run(145, 94)]);
        Assert.DoesNotContain(cmp.Metrics, m => m.Significant);
        Assert.StartsWith("Não detectamos ganho significativo", cmp.Verdict);
        Assert.NotEmpty(cmp.Warnings);
    }

    [Fact]
    public void Ganho_grande_e_reportado_com_o_numero_medido()
    {
        var cmp = BenchmarkComparer.Compare([Run(142, 91)], [Run(151, 104)]);
        var avg = cmp.Metrics.Single(m => m.Metric == "FPS médio");
        Assert.True(avg.Improved);
        Assert.Equal(6.34, avg.DeltaPercent, 2);
        Assert.True(cmp.Metrics.Single(m => m.Metric == "1% low").Improved);
        Assert.StartsWith("Ganho significativo", cmp.Verdict);
    }

    [Fact]
    public void Com_repeticoes_usa_a_variancia_real()
    {
        // Ganho médio de ~3%, mas a variação entre rodadas é maior que isso.
        var noisy = BenchmarkComparer.Compare([Run(140, 90), Run(150, 99), Run(135, 85)], [Run(149, 95), Run(138, 88), Run(151, 100)]);
        Assert.False(noisy.Metrics.Single(m => m.Metric == "FPS médio").Significant);

        // Mesmo ganho de ~3%, com rodadas consistentes: aí é real.
        var stable = BenchmarkComparer.Compare([Run(140, 90), Run(140.5, 90.2), Run(139.8, 89.9)], [Run(144.5, 93), Run(144.2, 93.1), Run(144.8, 92.8)]);
        Assert.True(stable.Metrics.Single(m => m.Metric == "FPS médio").Improved);
    }

    [Fact]
    public void Piora_e_reportada_e_sugere_rollback()
    {
        var cmp = BenchmarkComparer.Compare([Run(150, 100)], [Run(130, 80)]);
        Assert.Contains("rollback", cmp.Verdict);
    }
}
