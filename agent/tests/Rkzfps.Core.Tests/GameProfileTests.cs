using Rkzfps.Core.Diagnostics;
using Rkzfps.Core.Games;

namespace Rkzfps.Core.Tests;

public class GameProfileTests
{
    private static readonly IReadOnlyList<GameProfile> Profiles = TestData.Games();

    [Fact]
    public void Catalogo_de_jogos_cobre_os_populares()
    {
        var ids = Profiles.Select(p => p.Id).ToHashSet();
        Assert.Superset(new HashSet<string> { "cs2", "fortnite", "minecraft", "eafc", "valorant", "lol", "roblox", "gta5", "apex", "warzone" }, ids);
        Assert.Equal(ids.Count, Profiles.Count); // id único
    }

    [Fact]
    public void Todo_jogo_tem_executavel_medido_e_metodo_de_comparacao()
    {
        Assert.All(Profiles, p =>
        {
            Assert.NotEmpty(p.Benchmark.MeasuredProcesses(p.Detect));
            Assert.False(string.IsNullOrWhiteSpace(p.Benchmark.Method), p.Id);
            Assert.NotEmpty(p.RecommendedSettings);
        });
    }

    [Fact]
    public void Executavel_medido_e_sempre_reconhecido_como_jogo()
    {
        // Senão o "fechar programas em segundo plano" ofereceria fechar o jogo.
        foreach (var p in Profiles)
            foreach (var exe in p.Benchmark.MeasuredProcesses(p.Detect))
                Assert.True(ProcessClassifier.Classify(exe) == ProcessCategory.Game, $"{p.Id}: {exe}");
    }

    [Fact]
    public void Nenhum_executavel_e_de_dois_jogos()
    {
        var owners = Profiles.SelectMany(p => p.Benchmark.MeasuredProcesses(p.Detect).Select(e => (Exe: e.ToLowerInvariant(), p.Id)))
            .GroupBy(x => x.Exe).Where(g => g.Select(x => x.Id).Distinct().Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(owners);
    }

    [Fact]
    public void Launcher_nao_entra_na_medicao()
    {
        var mc = Profiles.Single(p => p.Id == "minecraft");
        Assert.Equal(["javaw"], mc.Benchmark.MeasuredProcesses(mc.Detect));
        Assert.Equal("Minecraft", mc.Benchmark.WindowTitle);
        var fortnite = Profiles.Single(p => p.Id == "fortnite");
        Assert.DoesNotContain("FortniteLauncher", fortnite.Benchmark.MeasuredProcesses(fortnite.Detect));
    }

    [Fact]
    public void FC_mede_o_ano_novo_e_o_anterior()
    {
        var fc = Profiles.Single(p => p.Id == "eafc");
        Assert.Contains("FC27", fc.Benchmark.MeasuredProcesses(fc.Detect));
        Assert.Contains("FC26", fc.Benchmark.MeasuredProcesses(fc.Detect));
        // Um FC de ano futuro também é protegido como jogo, pela regra do ano no nome.
        Assert.Equal(ProcessCategory.Game, ProcessClassifier.Classify("FC28"));
    }

    [Fact]
    public void Perfil_so_de_medicao_nao_edita_arquivo_do_jogo()
    {
        // Sem formato validado, nenhum arquivo do jogo é tocado: sem config,
        // sem preset, sem correção automática.
        foreach (var p in Profiles.Where(p => p.Config is null))
        {
            Assert.Empty(p.Presets);
            Assert.DoesNotContain(p.SettingChecks, c => c.FixValue is not null);
        }
    }

    [Fact]
    public void Otimizacoes_citadas_no_perfil_existem_no_catalogo()
    {
        var catalog = TestData.Catalog();
        foreach (var p in Profiles)
            foreach (var id in p.Optimizations)
                Assert.True(catalog.Find(id) is not null, $"{p.Id}: {id}");
    }
}
