using Fpsx.Core.Engine;
using Fpsx.Core.Model;
using Fpsx.Core.Optimizations;

namespace Fpsx.Core.Tests;

public class GraphicsProfileTests
{
    // PC forte: o preset automatico nao oferece nada, mas a pessoa pode escolher.
    private static SystemSnapshot Forte(Dictionary<string, string>? config = null)
    {
        var cs2 = GamePresetTests.WeakWithGames().Games.Single(g => g.GameId == "cs2");
        return Pc.Healthy() with { Games = [config is null ? cs2 : cs2 with { Config = config }] };
    }

    [Fact]
    public void Oferece_todos_os_perfis_do_jogo_em_qualquer_PC_e_passa_na_SafetyPolicy()
    {
        var e = new GraphicsProfileOptimization().Evaluate(new EvaluationContext(Forte(), TestData.Games()));
        Assert.Equal(Decision.Optional, e.Decision);
        Assert.Contains(e.Proposals, p => p.Id == "game-graphics-profile:cs2:pc-fraco");
        Assert.Contains(e.Proposals, p => p.Id == "game-graphics-profile:cs2:equilibrado");
        foreach (var c in e.Proposals.SelectMany(p => p.Changes))
            SafetyPolicy.Validate(c);
    }

    [Fact]
    public void Nunca_entra_na_selecao_automatica_nem_no_Otimizar_este_jogo()
    {
        var scan = TestData.Engine().Evaluate(Forte(), "competitive", "ultimate");
        Assert.False(scan.Optimizations.Single(o => o.Definition.Id == "game-graphics-profile").AutoSelected);
        Assert.DoesNotContain(GameTuning.ProposalIdsFor(scan, "cs2"), id => id.StartsWith("game-graphics-profile", StringComparison.Ordinal));
    }

    [Fact]
    public void Perfil_ja_no_arquivo_do_jogo_aparece_ativo_e_sem_nada_a_aplicar()
    {
        var cs2 = TestData.Games().Single(g => g.Id == "cs2");
        var leve = cs2.Presets.Single(p => p.Id == "pc-fraco");
        var config = GamePresetTests.WeakWithGames().Games.Single(g => g.GameId == "cs2").Config
            .ToDictionary(kv => kv.Key, kv => leve.Settings.TryGetValue(kv.Key, out var v) ? v : kv.Value);

        var scan = TestData.Engine().Evaluate(Forte(config), "competitive", "ultimate");
        var perfis = GameTuning.Profiles(scan, cs2);
        var ativo = perfis.Single(p => p.PresetId == "pc-fraco");
        Assert.True(ativo.IsActive);
        Assert.Null(ativo.ProposalId);
        Assert.Equal("Máximo FPS", ativo.Label);
        Assert.False(perfis.Single(p => p.PresetId == "equilibrado").IsActive);
    }
}
