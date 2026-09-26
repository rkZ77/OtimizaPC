using Fpsx.Core.Engine;
using Fpsx.Core.Model;

namespace Fpsx.Core.Tests;

public class GameTuningTests
{
    private const long Gb = 1024L * 1024 * 1024;

    /// <summary>PC intermediário (16 GB, placa de 4 GB, 8 threads) com CS2 pesado e V-Sync ligado.</summary>
    private static SystemSnapshot MidPcWithCs2() => Pc.WithCs2(Pc.Healthy() with
    {
        Gpus = [new GpuInfo { Name = "GTX 1650", Vendor = GpuVendor.Nvidia, VramBytes = 4 * Gb }],
        Cpu = Pc.Healthy().Cpu! with { Threads = 8 },
    }, new Dictionary<string, string>
    {
        ["setting.mat_vsync"] = "1", ["setting.msaa_samples"] = "8", ["setting.videocfg_shadow_quality"] = "3",
        ["setting.videocfg_ao_detail"] = "2", ["setting.videocfg_texture_detail"] = "0",
    });

    [Fact]
    public void PC_intermediario_recebe_o_equilibrado_opcional_e_nunca_sobe_qualidade()
    {
        var scan = TestData.Engine().Evaluate(MidPcWithCs2(), "gaming", "pro");
        var preset = scan.Optimizations.Single(o => o.Definition.Id == "game-preset-low-end");
        Assert.Equal(Decision.Optional, preset.Decision);
        var changes = preset.Evaluation.Proposals.Single().Changes.Cast<GameConfigChange>().ToDictionary(c => c.Key, c => c.Value);
        Assert.Equal("2", changes["setting.msaa_samples"]);
        Assert.Equal("1", changes["setting.videocfg_shadow_quality"]);
        // Texturas já estavam no mínimo (0): o equilibrado (1) não sobe.
        Assert.False(changes.ContainsKey("setting.videocfg_texture_detail"));
    }

    [Fact]
    public void Otimizar_este_jogo_junta_correcoes_e_preset_so_daquele_jogo()
    {
        var scan = TestData.Engine().Evaluate(MidPcWithCs2(), "gaming", "pro");
        var ids = GameTuning.ProposalIdsFor(scan, "cs2");
        Assert.Contains("game-settings-fix:cs2:setting.mat_vsync", ids);
        Assert.Contains("game-preset-low-end:cs2:equilibrado", ids);
        Assert.Empty(GameTuning.ProposalIdsFor(scan, "fortnite"));
    }

    [Fact]
    public void Voltar_como_era_desfaz_so_as_alteracoes_do_jogo()
    {
        var sys = new FakeSystem();
        foreach (var (k, v) in MidPcWithCs2().Games[0].Config)
            sys.GameConfig[k] = v;
        var store = new SessionStore(TestData.TempDir());
        var scan = TestData.Engine().Evaluate(MidPcWithCs2() with { Gaming = new GamingFeatures { AutoGameModeValue = 0 } }, "gaming", "pro");

        // Uma sessão com o jogo e uma otimização do Windows juntas.
        var ids = GameTuning.ProposalIdsFor(scan, "cs2").Append("game-mode-enable").ToList();
        var session = new OptimizationEngine(sys, store).Apply(scan, ids, new ApplyOptions());
        Assert.Equal("0", sys.GameConfig["setting.mat_vsync"]);

        var applied = GameTuning.AppliedChanges(store.All(), "cs2");
        Assert.NotEmpty(applied);
        var manager = new RollbackManager(sys, store);
        foreach (var (sid, cid, _) in applied)
            manager.RollbackChange(sid, cid, force: false);

        Assert.Equal("1", sys.GameConfig["setting.mat_vsync"]);
        Assert.Equal("8", sys.GameConfig["setting.msaa_samples"]);
        // O Game Mode, que não é do jogo, continua aplicado.
        Assert.Equal("1", sys.ReadRegistry(RegistryRoot.CurrentUser, Optimizations.RegistryPaths.GameBar, "AutoGameModeEnabled")!.Data);
        Assert.Empty(GameTuning.AppliedChanges(store.All(), "cs2"));
    }

    [Theory]
    [InlineData("\"true\"", true)]   // nuvens completas -> rápidas: mais leve
    [InlineData("\"fast\"", false)]  // já está no alvo
    [InlineData("\"false\"", false)] // desligadas: já é mais leve, não sobe
    [InlineData("\"estranho\"", false)]
    public void Valor_de_texto_so_muda_se_for_mais_pesado(string current, bool applies)
    {
        var preset = TestData.Games().Single(g => g.Id == "minecraft").Presets.Single(p => p.Id == "equilibrado");
        Assert.Equal(applies, preset.ShouldApply("renderClouds", current));
    }
}
