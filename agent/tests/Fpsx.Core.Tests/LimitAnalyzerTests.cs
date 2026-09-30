using System.Text.Json;
using Fpsx.Core.Benchmark;
using Fpsx.Core.Diagnostics;
using Fpsx.Core.Games;
using Fpsx.Core.Json;

namespace Fpsx.Core.Tests;

public class LimitAnalyzerTests
{
    // Partida de 10 min a ~100 FPS, monitor de 144 Hz, FPS variando com a cena
    // (sem cara de limite). Amostras a cada 5 s a partir do segundo 0.
    private static GameplaySession Match(Func<int, LoadSample, LoadSample> shape, int? hz = 144, IReadOnlyList<FpsPoint>? timeline = null, HardwareSummary? hw = null)
    {
        var frames = Enumerable.Range(0, 60_000).Select(i => i % 50 == 0 ? 14.0 : 9.9).ToList();
        var load = Enumerable.Range(1, 120).Select(i => i * 5).Select(t => shape(t, new LoadSample(t, 40, 70, null, 0) { Foreground = true, CpuMaxCore = 60 })).ToList();
        return new GameplaySession
        {
            Id = "s", GameId = "cs2", GameName = "Counter-Strike 2",
            Stats = FrameStats.From(frames),
            DisplayHz = hz,
            Timeline = timeline ?? Enumerable.Range(0, 600).Select(t => new FpsPoint(t, 80 + t % 40, 60)).ToList(),
            Load = load,
            Hardware = hw,
        };
    }

    private static LimitKind Kind(GameplaySession s) => LimitAnalyzer.Diagnose(s).Kind;

    [Fact]
    public void Partida_antiga_sem_leitura_do_pc_e_dados_insuficientes()
    {
        var d = LimitAnalyzer.Diagnose(Match((_, s) => s) with { Load = [] });
        Assert.Equal(LimitKind.Insufficient, d.Kind);
        Assert.Equal("DADOS INSUFICIENTES", d.Title);
        // O FPS medido continua aparecendo, marcado como calculado.
        Assert.Contains(d.Evidence, e => e.Kind == EvidenceKind.Calculated && e.Text.Contains("1% low"));
    }

    [Fact]
    public void Menos_de_um_minuto_de_leitura_nao_conclui()
    {
        var s = Match((_, x) => x with { Gpu = 99 });
        Assert.Equal(LimitKind.Insufficient, Kind(s with { Load = s.Load.Where(l => l.T is >= 60 and < 100).ToList() }));
    }

    [Fact]
    public void Aquecimento_e_jogo_minimizado_nao_entram()
    {
        var s = Match((t, x) => x with { Foreground = t < 400 && t >= 60 ? false : true, Gpu = 99 });
        // Só sobram as amostras de 400 s a 600 s (41): antes disso, carregamento ou jogo minimizado.
        Assert.Equal(41, LimitAnalyzer.InGame(s).Count);
        Assert.All(LimitAnalyzer.InGame(s), l => Assert.True(l.T >= 400));
    }

    [Fact]
    public void Cpu_limita_com_um_nucleo_cheio_mesmo_com_uso_total_baixo()
    {
        var d = LimitAnalyzer.Diagnose(Match((_, x) => x with { Cpu = 35, CpuMaxCore = 97, Gpu = 60 }));
        Assert.Equal(LimitKind.Cpu, d.Kind);
        Assert.Contains("35%", d.Why);
        Assert.Contains("processador com mais desempenho por núcleo", d.Advice);
    }

    [Fact]
    public void Thread_do_jogo_no_limite_tambem_aponta_cpu()
    {
        Assert.Equal(LimitKind.Cpu, Kind(Match((_, x) => x with { CpuMaxCore = 70, GameThreadMax = 98, Gpu = 55 })));
    }

    [Fact]
    public void Gpu_limita_com_placa_cheia_e_nucleos_folgados()
    {
        Assert.Equal(LimitKind.Gpu, Kind(Match((_, x) => x with { Gpu = 99, CpuMaxCore = 65 })));
    }

    [Fact]
    public void Uma_metrica_so_nao_vira_gargalo()
    {
        // GPU a 99% sem leitura por núcleo: não dá para descartar a CPU.
        Assert.Equal(LimitKind.Insufficient, Kind(Match((_, x) => x with { Gpu = 99, CpuMaxCore = null })));
        // Núcleo a 97% com a GPU também cheia: nenhum dos dois é claramente o limite.
        Assert.Equal(LimitKind.Balanced, Kind(Match((_, x) => x with { Gpu = 97, CpuMaxCore = 97 })));
    }

    [Fact]
    public void Fps_preso_na_taxa_do_monitor_e_configuracao_nao_peca()
    {
        var d = LimitAnalyzer.Diagnose(Match((_, x) => x with { Gpu = 99, CpuMaxCore = 50 }, hz: 100));
        Assert.Equal(LimitKind.Software, d.Kind);
        Assert.Contains("100 Hz", d.Why);
    }

    [Fact]
    public void Fps_reto_com_pc_folgado_e_limite_de_fps()
    {
        var flat = Enumerable.Range(0, 600).Select(t => new FpsPoint(t, 100, 90)).ToList();
        Assert.Equal(LimitKind.Software, Kind(Match((_, x) => x with { Gpu = 50, CpuMaxCore = 50 }, timeline: flat)));
    }

    [Fact]
    public void Temperatura_exige_calor_e_clock_caindo_juntos()
    {
        Assert.Equal(LimitKind.Temperature, Kind(Match((_, x) => x with { Gpu = 98, GpuTempC = 88, GpuClockPercent = 70 })));
        // Quente, mas sem perder clock: não é thermal throttling.
        Assert.NotEqual(LimitKind.Temperature, Kind(Match((_, x) => x with { Gpu = 98, GpuTempC = 88, GpuClockPercent = 99 })));
        // Clock contido sem leitura de temperatura: não afirma que é calor.
        Assert.NotEqual(LimitKind.Temperature, Kind(Match((_, x) => x with { CpuPerfLimit = 70, CpuTempC = null })));
        Assert.Equal(LimitKind.Temperature, Kind(Match((_, x) => x with { CpuPerfLimit = 70, CpuTempC = 96 })));
    }

    [Fact]
    public void Vram_cheia_transbordando_para_a_ram()
    {
        var hw = new HardwareSummary { VramGb = 8 };
        Assert.Equal(LimitKind.Vram, Kind(Match((t, x) => x with { VramUsedMb = 8000, SharedGpuMb = t < 100 ? 200 : 1500 }, hw: hw)));
        // Cheia mas sem transbordar e sem travada: é só o jogo usando a memória que tem.
        Assert.NotEqual(LimitKind.Vram, Kind(Match((_, x) => x with { VramUsedMb = 8000, SharedGpuMb = 200 }, hw: hw)));
        // GPU integrada usa a RAM por natureza: a regra não se aplica.
        Assert.NotEqual(LimitKind.Vram, Kind(Match((t, x) => x with { VramUsedMb = 8000, SharedGpuMb = t < 100 ? 200 : 1500 }, hw: hw with { GpuIntegrated = true })));
    }

    [Fact]
    public void Ram_exige_memoria_cheia_e_leitura_do_disco()
    {
        Assert.Equal(LimitKind.Ram, Kind(Match((_, x) => x with { RamPercent = 95, HardFaultsPerSec = 3000 })));
        Assert.NotEqual(LimitKind.Ram, Kind(Match((_, x) => x with { RamPercent = 95, HardFaultsPerSec = 10 })));
    }

    [Fact]
    public void Armazenamento_quando_as_quedas_batem_com_disco_no_limite()
    {
        int[] drops = [100, 200, 300, 400];
        var timeline = Enumerable.Range(0, 600).Select(t => new FpsPoint(t, 80 + t % 40, drops.Contains(t) ? 15 : 60)).ToList();
        var s = Match((t, x) => x with { DiskActivePercent = drops.Contains(t) ? 100 : 5 }, timeline: timeline);
        Assert.Equal(LimitKind.Storage, Kind(s));
    }

    [Fact]
    public void Pc_folgado_e_fps_perto_do_monitor_e_equilibrado()
    {
        Assert.Equal(LimitKind.Balanced, Kind(Match((_, x) => x with { Gpu = 80, CpuMaxCore = 75 }, hz: 110)));
    }

    [Fact]
    public void Metrica_nao_lida_aparece_como_nao_disponivel()
    {
        var d = LimitAnalyzer.Diagnose(Match((_, x) => x with { Gpu = 99 }));
        Assert.Contains(d.Evidence, e => e.Kind == EvidenceKind.Unavailable && e.Text.StartsWith("Temperatura da placa de vídeo não disponível"));
        Assert.Contains(d.Evidence, e => e.Kind == EvidenceKind.Unavailable && e.Text.StartsWith("Uso por thread do jogo não disponível"));
        Assert.Contains(d.Evidence, e => e.Kind == EvidenceKind.Measured && e.Text.StartsWith("Placa de vídeo: 99%"));
    }

    [Fact]
    public void Partida_antiga_explica_que_a_leitura_nao_existia_e_nao_culpa_driver()
    {
        // Amostras no formato antigo: sem primeiro plano nem leituras novas.
        var old = Match((_, x) => new LoadSample(x.T, 58, 96, null, 0));
        var d = LimitAnalyzer.Diagnose(old);
        Assert.Equal(LimitKind.Insufficient, d.Kind);
        Assert.Contains("antes do diagnóstico completo", d.Why);
        Assert.DoesNotContain(d.Evidence, e => e.Text.Contains("anti-cheat") || e.Text.Contains("driver não informa"));
        Assert.Contains(d.Evidence, e => e.Kind == EvidenceKind.Unavailable && e.Text.Contains("medida antes"));
        Assert.Contains(d.Evidence, e => e.Kind == EvidenceKind.Measured && e.Text.StartsWith("Placa de vídeo: 96%"));
    }

    [Fact]
    public void Resumo_da_partida_so_com_o_que_foi_lido()
    {
        var h = SessionHealth.From(Match((t, x) => x with { GpuTempC = t % 2 == 0 ? 70 : 80 }));
        Assert.Equal(75, h.GpuTempAvg!.Value, 1);
        Assert.Equal(80, h.GpuTempMax);
        Assert.Null(h.CpuTempMax);
    }

    [Fact]
    public void Sugestao_de_upgrade_usa_o_diagnostico_completo()
    {
        var cpu = Match((_, x) => x with { Cpu = 35, CpuMaxCore = 97, Gpu = 60 });
        var games = UpgradeEvidence.Games([cpu, cpu with { Id = "b" }]);
        Assert.Equal(LimitKind.Cpu, games[0].Diagnosed);
        // O uso médio sozinho (CPU 35%) diria "equilibrado"; o diagnóstico vê o núcleo cheio.
        Assert.Equal(Bottleneck.Cpu, UpgradeEvidence.Verdict(games));
    }

    [Fact]
    public void Comparacao_mostra_uso_de_cpu_e_gpu_como_contexto()
    {
        var pivot = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var before = Match((_, x) => x) with { Id = "a", StartedAt = pivot.AddHours(-2), EndedAt = pivot.AddHours(-1), AvgCpuPercent = 60, AvgGpuPercent = 90 };
        var after = before with { Id = "b", StartedAt = pivot.AddHours(1), EndedAt = pivot.AddHours(2), AvgCpuPercent = 50, AvgGpuPercent = 97 };
        var cmp = GameplayComparer.Compare([before, after], "cs2", "Counter-Strike 2", pivot, "x");
        Assert.Equal((60.0, 50.0), (cmp.Cpu.Before!.Value, cmp.Cpu.After!.Value));
        Assert.Equal((90.0, 97.0), (cmp.Gpu.Before!.Value, cmp.Gpu.After!.Value));
    }

    [Fact]
    public void Partidas_de_antes_descartadas_pelo_cache_dizem_o_motivo()
    {
        var pivot = new DateTimeOffset(2026, 9, 29, 18, 58, 0, TimeSpan.Zero);
        var clear = pivot.AddDays(-2);
        var before = Match((_, x) => x) with { Id = "a", GameId = "cs2", StartedAt = clear.AddMinutes(20), EndedAt = clear.AddMinutes(50) };
        var after = before with { Id = "b", StartedAt = pivot.AddHours(3), EndedAt = pivot.AddHours(4) };
        var cmp = GameplayComparer.Compare([before, after], "cs2", "Counter-Strike 2", pivot, "x", [clear]);
        Assert.False(cmp.Ready);
        Assert.Contains("logo depois de limpar o cache de shaders", cmp.Status);
        Assert.DoesNotContain("Não há partida", cmp.Status);
    }

    [Fact]
    public void Fechar_programa_nao_vira_marco_de_otimizacao()
    {
        var close = new Fpsx.Core.Engine.SessionRecord
        {
            Id = "s1", Status = Fpsx.Core.Engine.SessionStatus.Completed,
            Changes = [new Fpsx.Core.Engine.ChangeRecord { Status = Fpsx.Core.Engine.ChangeStatus.Applied, Applied = new Fpsx.Core.Model.ProcessCloseChange(11600, "chrome") }],
        };
        Assert.Empty(GameplayComparer.Pivots([close]));
    }

    [Fact]
    public void Partida_salva_antes_das_leituras_novas_ainda_abre()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fpsx-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new GameplayStore(dir);
            Directory.CreateDirectory(store.Dir);
            File.WriteAllText(Path.Combine(store.Dir, "old.json"),
                """{"id":"old","game_id":"cs2","game_name":"CS2","stats":{"frames":1000,"avg_fps":100},"load":[{"t":65,"cpu":50,"gpu":90,"app":null,"app_cpu":0}]}""");
            var fresh = Match((_, x) => x with { GpuTempC = 70, VramUsedMb = 4000 }) with { Id = "new", ScreenWidth = 1920, ScreenHeight = 1080, GameSettings = new Dictionary<string, string> { ["setting.mat_vsync"] = "0" } };
            store.Save(fresh);

            var all = store.All();
            var old = all.Single(s => s.Id == "old");
            Assert.Null(old.Load[0].CpuMaxCore);
            Assert.Null(old.ScreenWidth);
            var back = all.Single(s => s.Id == "new");
            Assert.Equal(70, back.Load[0].GpuTempC);
            Assert.Equal("0", back.GameSettings["setting.mat_vsync"]);
            Assert.Equal(1080, back.ScreenHeight);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class CounterMathTests
{
    [Fact]
    public void Cpu_pega_o_nucleo_mais_ocupado_e_ignora_os_totais()
    {
        var r = CounterMath.Cpu([
            new("_Total", 30, 45, 120, 100, 3600),
            new("0,_Total", 30, 45, 120, 100, 3600),
            new("0,0", 95, null, null, null, null),
            new("0,1", 12, null, null, null, null),
        ]);
        Assert.Equal(45, r.Total);
        Assert.Equal(95, r.MaxCore);
        Assert.Equal(4320, r.ClockMhz);
        Assert.Equal(100, r.PerfLimit);
    }

    [Fact]
    public void Cpu_sem_contador_fica_nulo_e_nao_zero()
    {
        var r = CounterMath.Cpu([]);
        Assert.Null(r.Total);
        Assert.Null(r.MaxCore);
        Assert.Null(r.ClockMhz);
    }

    [Fact]
    public void Luid_do_nome_do_contador()
    {
        Assert.Equal((0x109D3u, 0), CounterMath.Luid("pid_1496_luid_0x00000000_0x000109D3_phys_0_eng_0_engtype_3D"));
        Assert.Equal("00000000000109D3", CounterMath.LuidKey("luid_0x00000000_0x000109D3_phys_0"));
        Assert.Null(CounterMath.Luid("_Total"));
        Assert.Equal("", CounterMath.LuidKey("sem luid"));
    }

    [Fact]
    public void Thread_mais_ocupada_so_com_base_nas_duas_leituras()
    {
        var before = new Dictionary<int, TimeSpan> { [1] = TimeSpan.FromSeconds(10), [2] = TimeSpan.FromSeconds(3) };
        var after = new Dictionary<int, TimeSpan> { [1] = TimeSpan.FromSeconds(14.5), [2] = TimeSpan.FromSeconds(4), [3] = TimeSpan.FromSeconds(50) };
        Assert.Equal(90, CounterMath.BusiestThread(before, after, 5)!.Value, 3);
        Assert.Null(CounterMath.BusiestThread(new Dictionary<int, TimeSpan>(), after, 5));
    }

    [Fact]
    public void Temperatura_do_driver_zero_e_nao_informada()
    {
        Assert.Equal(51.0, CounterMath.DeciCelsius(510));
        Assert.Null(CounterMath.DeciCelsius(0));
        Assert.Null(CounterMath.DeciCelsius(5000));
    }
}

public class GameDetectionTests
{
    [Theory]
    [InlineData("EldenRing", @"D:\Games\ELDEN RING\eldenring.exe", true, 60.0, true)]
    [InlineData("EldenRing", null, true, 60.0, true)]
    [InlineData("EldenRing", @"D:\Games\eldenring.exe", false, 60.0, false)]
    [InlineData("EldenRing", @"D:\Games\eldenring.exe", true, 3.0, false)]
    [InlineData("EldenRing", @"D:\Games\eldenring.exe", true, null, false)]
    [InlineData("chrome", @"C:\Program Files\Google\chrome.exe", true, 60.0, false)]
    [InlineData("vlc", @"C:\Program Files\VideoLAN\vlc.exe", true, 60.0, false)]
    [InlineData("steam", @"C:\Steam\steam.exe", true, 60.0, false)]
    [InlineData("dwm", null, true, 60.0, false)]
    public void Jogo_sem_perfil_exige_tela_cheia_e_gpu(string name, string? path, bool fullscreen, double? gpu, bool expected) =>
        Assert.Equal(expected, ProcessClassifier.IsLikelyGame(name, path, fullscreen, gpu));

    [Fact]
    public void Programa_da_pasta_do_windows_nao_e_jogo()
    {
        var windows = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        Assert.False(ProcessClassifier.IsLikelyGame("mmc", Path.Combine(windows, "System32", "mmc.exe"), true, 50));
    }

    [Fact]
    public void Configuracao_guardada_na_partida_e_so_a_do_perfil()
    {
        var profile = new GameProfile
        {
            RecommendedSettings = new Dictionary<string, string> { ["a"] = "1" },
            Presets = [new GamePreset { Settings = new Dictionary<string, string> { ["b"] = "2", ["A"] = "3" } }],
            SettingChecks = [new SettingCheck { Key = "c" }],
            RefreshRateCheck = new RefreshRateCheck { WidthKey = "w", HeightKey = "h", NumeratorKey = "n", DenominatorKey = "" },
        };
        Assert.Equal(["a", "b", "c", "w", "h", "n"], profile.GraphicsKeys());
    }
}
