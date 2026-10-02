using Rkzfps.Core.Diagnostics;
using Rkzfps.Core.Engine;
using Rkzfps.Core.Games;
using Rkzfps.Core.Hardware;
using Rkzfps.Core.Model;
using Rkzfps.Core.Optimizations;

namespace Rkzfps.Core.Tests;

public class GameTipsTests
{
    private const long Gb = 1024L * 1024 * 1024;
    private static readonly IReadOnlyList<GameProfile> Profiles = TestData.Games();
    private static GameProfile Game(string id) => Profiles.Single(p => p.Id == id);

    private static SystemSnapshot Pc(string gpu, long vram, int threads, double ram, int hz) => Tests.Pc.Healthy() with
    {
        Cpu = Tests.Pc.Healthy().Cpu! with { Threads = threads },
        Gpus = [new GpuInfo { Name = gpu, Vendor = gpu.Contains("Radeon") ? GpuVendor.Amd : GpuVendor.Nvidia, VramBytes = vram * Gb }],
        Memory = new MemoryInfo { TotalBytes = (long)(ram * Gb), AvailableBytes = Gb, PagefilePresent = true },
        Displays = [new DisplayInfo { DeviceName = "d", IsPrimary = true, CurrentHz = hz, MaxHzAtCurrentResolution = hz }],
    };

    private static readonly SystemSnapshot Forte = Pc("NVIDIA GeForce RTX 5060 Ti", 16, 16, 32, 240);
    private static readonly SystemSnapshot Fraco = Pc("NVIDIA GeForce GTX 1650", 4, 4, 8, 60);
    private static readonly SystemSnapshot RadeonMedio = Pc("AMD Radeon RX 580 Series", 8, 8, 16, 144);

    private static IReadOnlyList<TipResult> Tips(string game, SystemSnapshot s, UserPreferences? prefs)
    {
        var tuning = ProfileResolver.Resolve(prefs, s, "gaming");
        return GameTips.For(Game(game), s, tuning, HardwareCatalog.Default.Recognize(s));
    }

    [Fact]
    public void PC_forte_com_mais_FPS_recebe_latencia_e_nao_baixa_imagem()
    {
        var tips = Tips("cs2", Forte, new UserPreferences { Goal = Goal.MoreFps });
        var ids = tips.Select(t => t.Id).ToList();
        // Latência primeiro, com Reflex (a placa tem) e a taxa do monitor.
        Assert.Equal("vsync", ids[0]);
        Assert.Contains("reflex", ids);
        Assert.Contains(tips, t => t.Id == "hz" && t.Value == "240 Hz");
        // Nada que piore a imagem.
        Assert.DoesNotContain(ids, id => id is "ao" or "msaa-light" or "msaa-medium" or "shadows" or "particles");
    }

    [Fact]
    public void PC_fraco_com_desempenho_recebe_cortes_de_imagem()
    {
        var ids = Tips("cs2", Fraco, new UserPreferences { Goal = Goal.MoreFps, Graphics = GraphicsPreference.Performance }).Select(t => t.Id).ToList();
        Assert.Contains("msaa-medium", ids);
        Assert.Contains("ao", ids);
        Assert.DoesNotContain("msaa-light", ids);
    }

    [Fact]
    public void Quem_prefere_qualidade_nao_recebe_corte_de_imagem_mesmo_no_PC_fraco()
    {
        var ids = Tips("fortnite", Fraco, new UserPreferences { Graphics = GraphicsPreference.Quality }).Select(t => t.Id).ToList();
        Assert.Contains("vsync", ids);
        Assert.DoesNotContain(ids, id => id is "shadows" or "gi" or "effects" or "render-mode");
    }

    [Fact]
    public void Recurso_da_placa_decide_a_dica()
    {
        // Radeon não recebe Reflex nem DLSS.
        var ids = Tips("fortnite", RadeonMedio, new UserPreferences { Goal = Goal.MoreFps, Graphics = GraphicsPreference.Performance }).Select(t => t.Id).ToList();
        Assert.DoesNotContain("reflex", ids);
        Assert.DoesNotContain("dlss", ids);
        // RTX intermediária com troca de imagem recebe DLSS.
        var rtx = Pc("NVIDIA GeForce RTX 3050", 8, 12, 16, 144);
        Assert.Contains("dlss", Tips("fortnite", rtx, new UserPreferences { Graphics = GraphicsPreference.Performance }).Select(t => t.Id));
    }

    [Fact]
    public void Usuario_antigo_sem_perguntas_tambem_recebe_dicas_pelo_nivel()
    {
        var ids = Tips("cs2", Fraco, null).Select(t => t.Id).ToList();
        Assert.Contains("vsync", ids);
        Assert.Contains("msaa-medium", ids);
    }

    [Fact]
    public void Emulador_respeita_o_tamanho_do_PC()
    {
        var forte = Tips("freefire-emulador", Forte, new UserPreferences()).Select(t => t.Id).ToList();
        Assert.Contains("cores", forte);
        Assert.DoesNotContain("cores-low", forte);
        var fraco = Tips("freefire-emulador", Fraco, new UserPreferences()).Select(t => t.Id).ToList();
        Assert.Contains("cores-low", fraco);
        Assert.Contains("ram-low", fraco);
        Assert.DoesNotContain("cores", fraco);
    }

    [Fact]
    public void Nunca_mais_que_o_limite_e_sem_placeholder_sobrando()
    {
        foreach (var p in Profiles.Where(p => p.Tips.Count > 0))
            foreach (var s in new[] { Forte, Fraco, RadeonMedio, Tests.Pc.Healthy() with { Displays = [] } })
            {
                var tips = GameTips.For(p, s, ProfileResolver.Resolve(new UserPreferences(), s, "gaming"), HardwareCatalog.Default.Recognize(s));
                Assert.True(tips.Count <= GameTips.MaxTips, p.Id);
                Assert.All(tips, t => Assert.DoesNotContain("{", t.Value + t.Why));
            }
    }

    [Fact]
    public void Toda_dica_e_coerente_e_segue_a_regra_de_texto()
    {
        var features = typeof(HardwareFeature).GetFields().Select(f => (string)f.GetValue(null)!).ToHashSet();
        foreach (var p in Profiles)
        {
            Assert.Equal(p.Tips.Count, p.Tips.Select(t => t.Id).Distinct().Count());
            foreach (var t in p.Tips)
            {
                Assert.False(string.IsNullOrWhiteSpace(t.Setting + t.Value + t.Why) || t.Setting == "" || t.Value == "" || t.Why == "", $"{p.Id}/{t.Id}");
                foreach (var text in new[] { t.Setting, t.Value, t.Why })
                {
                    Assert.DoesNotContain("—", text);
                    Assert.DoesNotContain("·", text);
                }
                if (t.When.GpuFeature is { } f)
                    Assert.Contains(f, features);
                if (t.When.GpuLacks is { } l)
                    Assert.Contains(l, features);
                // Sem Anti-Lag: no CS2 o Anti-Lag+ chegou a causar banimento pelo VAC.
                Assert.NotEqual(HardwareFeature.AntiLag, t.When.GpuFeature);
            }
        }
    }

    [Fact]
    public void Jogos_da_fase_1_e_os_novos_tem_dicas()
    {
        foreach (var id in new[] { "cs2", "valorant", "lol", "fortnite", "overwatch2", "freefire-emulador" })
            Assert.NotEmpty(Game(id).Tips);
    }

    [Fact]
    public void LoL_so_le_a_configuracao_e_nunca_gera_alteracao()
    {
        var lol = Game("lol");
        Assert.True(lol.Config!.ReadOnly);
        Assert.Empty(lol.Presets);
        Assert.DoesNotContain(lol.SettingChecks, c => c.FixValue is not null);
        // Mesmo com V-Sync ligado no arquivo, o diagnóstico aponta e nenhum handler propõe gravar.
        var s = Forte with { Games = [new GameInstall { GameId = "lol", Name = "League of Legends", ConfigPath = "game.cfg",
            Config = new Dictionary<string, string> { ["General|WaitForVerticalSync"] = "1" } }] };
        var scan = TestData.Engine().Evaluate(s, "gaming", "pro");
        Assert.Contains(scan.Findings, f => f.Title.StartsWith("League of Legends: V-Sync", StringComparison.Ordinal));
        Assert.DoesNotContain(scan.Optimizations.SelectMany(o => o.Evaluation.Proposals).SelectMany(p => p.Changes),
            c => c is GameConfigChange g && g.GameId == "lol");
    }

    [Theory]
    [InlineData("HD-Player")]
    [InlineData("dnplayer")]
    [InlineData("MEmu")]
    [InlineData("MuMuPlayer")]
    [InlineData("Overwatch")]
    public void Emulador_e_Overwatch_contam_como_jogo_e_nunca_sao_fechados(string exe)
    {
        Assert.Equal(ProcessCategory.Game, ProcessClassifier.Classify(exe));
    }
}
