using Rkzfps.Core.Diagnostics;
using Rkzfps.Core.Engine;
using Rkzfps.Core.Model;
using Rkzfps.Core.Optimizations;

namespace Rkzfps.Core.Tests;

public class ProfileResolverTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static readonly Dictionary<string, string> Cs2Pesado = new()
    {
        ["setting.mat_vsync"] = "1", ["setting.msaa_samples"] = "8", ["setting.videocfg_shadow_quality"] = "3",
        ["setting.videocfg_texture_detail"] = "2", ["setting.videocfg_particle_detail"] = "2", ["setting.videocfg_ao_detail"] = "2",
        ["setting.r_texturefilteringquality"] = "5", ["setting.shaderquality"] = "1",
    };

    /// <summary>Ryzen 7 5700X3D, RTX 5060 Ti 16 GB, 32 GB, monitor de 240 Hz.</summary>
    private static SystemSnapshot Forte(int hz = 240) => Pc.WithCs2(Pc.Healthy() with
    {
        Cpu = Pc.Healthy().Cpu! with { Name = "AMD Ryzen 7 5700X3D 8-Core Processor", Cores = 8, Threads = 16 },
        Gpus = [new GpuInfo { Name = "NVIDIA GeForce RTX 5060 Ti", Vendor = GpuVendor.Nvidia, VramBytes = 16 * Gb }],
        Memory = new MemoryInfo { TotalBytes = 32 * Gb, AvailableBytes = 20 * Gb, PagefilePresent = true },
        Displays = [new DisplayInfo { DeviceName = @"\\.\DISPLAY1", IsPrimary = true, Width = 1920, Height = 1080, CurrentHz = hz, MaxHzAtCurrentResolution = hz }],
    }, new(Cs2Pesado));

    /// <summary>Ryzen 3 de 4 threads, GTX 1650 4 GB, 8 GB, 60 Hz.</summary>
    private static SystemSnapshot Fraco() => Pc.WithCs2(Pc.Healthy() with
    {
        Cpu = Pc.Healthy().Cpu! with { Name = "AMD Ryzen 3 3200G", Cores = 4, Threads = 4 },
        Gpus = [new GpuInfo { Name = "NVIDIA GeForce GTX 1650", Vendor = GpuVendor.Nvidia, VramBytes = 4 * Gb }],
        Memory = new MemoryInfo { TotalBytes = 8 * Gb, AvailableBytes = 3 * Gb, PagefilePresent = true },
        Displays = [new DisplayInfo { DeviceName = @"\\.\DISPLAY1", IsPrimary = true, Width = 1920, Height = 1080, CurrentHz = 60, MaxHzAtCurrentResolution = 60 }],
    }, new(Cs2Pesado));

    /// <summary>16 GB, 8 threads, placa de 6 GB, 60 Hz: intermediário.</summary>
    private static SystemSnapshot Medio(int hz = 60) => Pc.WithCs2(Pc.Healthy() with
    {
        Cpu = Pc.Healthy().Cpu! with { Threads = 8 },
        Gpus = [new GpuInfo { Name = "NVIDIA GeForce RTX 2060", Vendor = GpuVendor.Nvidia, VramBytes = 6 * Gb }],
        Displays = [new DisplayInfo { DeviceName = @"\\.\DISPLAY1", IsPrimary = true, Width = 1920, Height = 1080, CurrentHz = hz, MaxHzAtCurrentResolution = hz }],
    }, new(Cs2Pesado));

    private static UserPreferences Prefs(Goal? goal = null, GraphicsPreference? g = null, ProfileChoice c = ProfileChoice.Automatic, PowerPreference? p = null) =>
        new() { Choice = c, Goal = goal, Graphics = g, Power = p };

    /// <summary>O fluxo inteiro do app: resolver, motor com o perfil interno, ajuste da preferência.</summary>
    private static (OptimizationProfile Profile, ScanResult Scan) Run(SystemSnapshot s, UserPreferences? prefs, string plan = "pro", string saved = "gaming")
    {
        var profile = ProfileResolver.Resolve(prefs, s, saved);
        return (profile, profile.ApplyTo(TestData.Engine().Evaluate(s, profile.CatalogProfileId, plan)));
    }

    private static OptimizationResult Item(ScanResult scan, string id) => scan.Optimizations.Single(o => o.Definition.Id == id);

    [Fact]
    public void PC_forte_com_mais_FPS_nao_piora_a_imagem_e_foca_em_latencia()
    {
        var (profile, scan) = Run(Forte(), Prefs(Goal.MoreFps, GraphicsPreference.Performance));

        Assert.Equal(HardwareTier.High, profile.Tier);
        Assert.Equal(GraphicsTradeoff.None, profile.GraphicsTradeoff);
        Assert.Equal(Priority.High, profile.LatencyPriority);
        Assert.Equal(Objective.MaximumPerformance, profile.Objective);
        Assert.Equal("high-end", profile.CatalogProfileId);
        Assert.Contains(profile.Reasons, r => r.Contains("forte") && r.Contains("não precisa piorar a imagem"));
        Assert.Contains(profile.Reasons, r => r.Contains("240 Hz"));

        // Nenhuma proposta gráfica; a correção de latência do jogo (V-Sync) continua recomendada.
        Assert.Empty(GameTuningPresets(scan));
        Assert.Equal(Decision.Recommended, Item(scan, "game-settings-fix").Decision);
        Assert.Contains("sem mexer na imagem", profile.Summary);
    }

    [Fact]
    public void PC_de_entrada_com_desempenho_maximo_recebe_o_preset_leve_que_pede_confirmacao()
    {
        var (profile, scan) = Run(Fraco(), Prefs(Goal.MoreFps, GraphicsPreference.Performance));

        Assert.Equal(HardwareTier.Low, profile.Tier);
        Assert.Equal(GraphicsTradeoff.Medium, profile.GraphicsTradeoff);
        Assert.Equal(Aggressiveness.High, profile.Aggressiveness);
        Assert.Equal("low-end", profile.CatalogProfileId);
        var preset = Item(scan, "game-preset-low-end");
        Assert.Equal(Decision.Recommended, preset.Decision);
        // Preset gráfico nunca é aplicado em silêncio: fora da seleção automática.
        Assert.False(preset.AutoSelected);
        Assert.True(preset.Definition.RequiresUserConfirmation);
    }

    [Fact]
    public void Mesmo_pedido_PCs_diferentes_decisoes_diferentes()
    {
        var prefs = Prefs(Goal.MoreFps);
        Assert.NotEqual(Run(Forte(), prefs).Profile.GraphicsTradeoff, Run(Fraco(), prefs).Profile.GraphicsTradeoff);
        Assert.NotEqual(Run(Forte(), prefs).Profile.CatalogProfileId, Run(Fraco(), prefs).Profile.CatalogProfileId);
    }

    [Fact]
    public void Quem_prefere_qualidade_no_PC_fraco_ve_o_preset_so_como_opcao()
    {
        var (profile, scan) = Run(Fraco(), Prefs(Goal.Balanced, GraphicsPreference.Quality));
        Assert.Equal(GraphicsTradeoff.None, profile.GraphicsTradeoff);
        var preset = Item(scan, "game-preset-low-end");
        Assert.Equal(Decision.Optional, preset.Decision);
        Assert.Equal(Decision.Optional, preset.Evaluation.Decision);
        Assert.False(preset.AutoSelected);
        Assert.Contains("qualidade de imagem", preset.Reason);
    }

    [Fact]
    public void PC_medio_so_promove_o_preset_quando_a_pessoa_aceita_trocar_imagem()
    {
        var antes = TestData.Engine().Evaluate(Medio(), "gaming", "pro");
        Assert.Equal(Decision.Optional, Item(antes, "game-preset-low-end").Decision);

        var (_, aceita) = Run(Medio(), Prefs(Goal.MoreFps, GraphicsPreference.Performance));
        Assert.Equal(Decision.Recommended, Item(aceita, "game-preset-low-end").Decision);
        Assert.False(Item(aceita, "game-preset-low-end").AutoSelected);

        var (_, equilibrio) = Run(Medio(), Prefs(Goal.Balanced));
        Assert.Equal(Decision.Optional, Item(equilibrio, "game-preset-low-end").Decision);
    }

    [Fact]
    public void Pular_tudo_usa_Automatico_pelo_nivel_do_PC_e_funciona_sem_resposta()
    {
        var pulou = new UserPreferences();
        Assert.Equal("high-end", Run(Forte(60), pulou).Profile.CatalogProfileId);
        Assert.Equal("gaming", Run(Medio(), pulou).Profile.CatalogProfileId);
        var (fraco, scan) = Run(Fraco(), pulou);
        Assert.Equal("low-end", fraco.CatalogProfileId);
        Assert.Equal(Objective.Balanced, fraco.Objective);
        Assert.Contains("equilibrado e seguro", fraco.Summary);
        // Sem resposta, o PC fraco continua recebendo o que o motor já recomendava.
        Assert.Equal(Decision.Recommended, Item(scan, "game-preset-low-end").Decision);
    }

    [Fact]
    public void Usuario_antigo_sem_respostas_continua_exatamente_como_antes()
    {
        foreach (var saved in new[] { "safe", "gaming", "competitive", "streaming", "low-end", "high-end", "custom" })
        {
            var s = Fraco();
            var antes = TestData.Engine().Evaluate(s, saved, "pro");
            var (profile, depois) = Run(s, null, saved: saved);
            Assert.True(profile.Legacy);
            Assert.Equal(saved, profile.CatalogProfileId);
            Assert.Equal(antes.Optimizations.Select(o => (o.Definition.Id, o.Decision, o.AutoSelected)),
                depois.Optimizations.Select(o => (o.Definition.Id, o.Decision, o.AutoSelected)));
        }
    }

    [Fact]
    public void Os_cinco_perfis_da_tela_apontam_para_perfis_internos_que_existem()
    {
        var catalog = TestData.Catalog();
        Assert.Equal("competitive", ProfileResolver.CatalogProfile(ProfileChoice.Performance, HardwareTier.Mid, "gaming"));
        Assert.Equal("gaming", ProfileResolver.CatalogProfile(ProfileChoice.Balanced, HardwareTier.Low, "gaming"));
        Assert.Equal("high-end", ProfileResolver.CatalogProfile(ProfileChoice.Quality, HardwareTier.Low, "gaming"));
        Assert.Equal("custom", ProfileResolver.CatalogProfile(ProfileChoice.Custom, HardwareTier.High, "gaming"));
        Assert.Equal("streaming", ProfileResolver.CatalogProfile(ProfileChoice.Advanced, HardwareTier.High, "streaming"));
        foreach (var tier in Enum.GetValues<HardwareTier>())
            foreach (var choice in Enum.GetValues<ProfileChoice>().Where(c => c != ProfileChoice.Advanced))
                Assert.NotNull(catalog.FindProfile(ProfileResolver.CatalogProfile(choice, tier, "gaming")));
        // Os 7 internos continuam no catálogo, streaming incluído.
        Assert.Equal(["safe", "gaming", "competitive", "streaming", "low-end", "high-end", "custom"], catalog.Profiles.Select(p => p.Id));
    }

    [Fact]
    public void Perfil_salvo_antigo_aparece_como_o_novo_equivalente()
    {
        Assert.Equal(ProfileChoice.Balanced, UserPreferences.FromLegacy("gaming"));
        Assert.Equal(ProfileChoice.Performance, UserPreferences.FromLegacy("competitive"));
        Assert.Equal(ProfileChoice.Quality, UserPreferences.FromLegacy("high-end"));
        Assert.Equal(ProfileChoice.Custom, UserPreferences.FromLegacy("custom"));
        Assert.Equal(ProfileChoice.Advanced, UserPreferences.FromLegacy("streaming"));
        Assert.Equal(ProfileChoice.Advanced, UserPreferences.FromLegacy("low-end"));
    }

    [Fact]
    public void Notebook_que_prefere_bateria_nao_troca_o_plano_de_energia_sozinho()
    {
        var s = Pc.Laptop(onAc: true) with { Power = Pc.Laptop(onAc: true).Power! with { ActiveSchemeGuid = PowerSchemes.PowerSaver } };
        var (bateria, scan) = Run(s, Prefs(Goal.Balanced, p: PowerPreference.Battery));
        Assert.True(bateria.PreferBattery);
        Assert.Equal(Priority.High, bateria.TemperaturePriority);
        var saver = Item(scan, "power-plan-leave-power-saver");
        Assert.NotEqual(Decision.Recommended, saver.Decision);
        Assert.False(saver.AutoSelected);

        var (desempenho, scan2) = Run(s, Prefs(Goal.Balanced, p: PowerPreference.Performance));
        Assert.False(desempenho.PreferBattery);
        Assert.Equal(Decision.Recommended, Item(scan2, "power-plan-leave-power-saver").Decision);
    }

    [Fact]
    public void Otimizar_este_jogo_respeita_quem_quer_manter_a_imagem()
    {
        var (quer, scanQ) = Run(Fraco(), Prefs(Goal.Balanced, GraphicsPreference.Quality));
        var ids = GameTuning.ProposalIdsFor(scanQ, "cs2", quer);
        Assert.DoesNotContain(ids, id => id.StartsWith("game-preset-low-end", StringComparison.Ordinal));
        // A correção de latência (V-Sync) continua.
        Assert.Contains(ids, id => id.StartsWith("game-settings-fix:cs2", StringComparison.Ordinal));

        var (aceita, scanA) = Run(Fraco(), Prefs(Goal.MoreFps, GraphicsPreference.Performance));
        Assert.Contains(GameTuning.ProposalIdsFor(scanA, "cs2", aceita), id => id.StartsWith("game-preset-low-end:cs2", StringComparison.Ordinal));

        // Sem perfil (chamada antiga) ou usuário antigo: igual a antes.
        var legacy = ProfileResolver.Resolve(null, Fraco(), "gaming");
        Assert.Equal(GameTuning.ProposalIdsFor(scanQ, "cs2"), GameTuning.ProposalIdsFor(scanQ, "cs2", legacy));
    }

    [Theory]
    [MemberData(nameof(Pc.Matrix), MemberType = typeof(Pc))]
    public void Preferencia_nunca_libera_bloqueado_nem_marca_selecao_automatica(string label, SystemSnapshot s)
    {
        _ = label;
        foreach (var plan in new[] { "free", "pro" })
            foreach (var prefs in AllPrefs())
            {
                var profile = ProfileResolver.Resolve(prefs, s, "gaming");
                var raw = TestData.Engine().Evaluate(s, profile.CatalogProfileId, plan);
                var adjusted = profile.ApplyTo(raw);
                foreach (var (a, b) in raw.Optimizations.Zip(adjusted.Optimizations))
                {
                    Assert.Equal(a.Definition.Id, b.Definition.Id);
                    Assert.Same(a.Evaluation.Proposals, b.Evaluation.Proposals);
                    if (a.Decision is not (Decision.Recommended or Decision.Optional))
                        Assert.Equal(a.Decision, b.Decision);
                    Assert.True(!b.AutoSelected || a.AutoSelected);
                }
            }
    }

    private static IEnumerable<UserPreferences> AllPrefs()
    {
        yield return new UserPreferences();
        foreach (var c in Enum.GetValues<ProfileChoice>().Where(c => c != ProfileChoice.Advanced))
            foreach (var g in new GraphicsPreference?[] { null, GraphicsPreference.Performance, GraphicsPreference.Quality })
                yield return Prefs(Goal.MoreFps, g, c, PowerPreference.Battery);
    }

    private static IEnumerable<string> GameTuningPresets(ScanResult scan) =>
        GameTuning.ProposalIdsFor(scan, "cs2").Where(id => id.StartsWith("game-preset-low-end", StringComparison.Ordinal));
}

public class OnboardingTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static SystemSnapshot Desktop(int hz, int threads = 16, long vramGb = 16) => Pc.Healthy() with
    {
        Cpu = Pc.Healthy().Cpu! with { Threads = threads },
        Gpus = [new GpuInfo { Name = "GPU", Vendor = GpuVendor.Nvidia, VramBytes = vramGb * Gb }],
        Memory = new MemoryInfo { TotalBytes = 32 * Gb, AvailableBytes = 20 * Gb, PagefilePresent = true },
        Displays = [new DisplayInfo { DeviceName = "d", IsPrimary = true, CurrentHz = hz, MaxHzAtCurrentResolution = hz }],
    };

    private static IEnumerable<string> Ids(SystemSnapshot s, UserPreferences? p = null) =>
        Onboarding.Contextual(s, p ?? new UserPreferences()).Select(q => q.Id);

    [Fact]
    public void A_principal_tem_as_quatro_opcoes()
    {
        Assert.Equal("O que você quer melhorar?", Onboarding.Main.Title);
        Assert.Equal(["Mais FPS", "Menos travadas", "Menor latência", "Equilíbrio"], Onboarding.Main.Options.Select(o => o.Label));
    }

    [Fact]
    public void So_pergunta_o_que_muda_alguma_decisao_neste_PC()
    {
        // PC forte em 60 Hz: não baixa gráfico, então não pergunta.
        Assert.Empty(Ids(Desktop(60)));
        // PC forte em 240 Hz: FPS ou imagem.
        Assert.Equal([Onboarding.PriorityQuestion], Ids(Desktop(240)));
        // PC de entrada: aceita reduzir a imagem?
        Assert.Equal([Onboarding.GraphicsQuestion], Ids(Desktop(240, threads: 4)));
        // Intermediário em 60 Hz: mesma pergunta.
        Assert.Equal([Onboarding.GraphicsQuestion], Ids(Desktop(60, threads: 8, vramGb: 6)));
        // Quem pediu equilíbrio já respondeu.
        Assert.Empty(Ids(Desktop(240), new UserPreferences { Goal = Goal.Balanced }));
        // Hardware não lido: não pergunta de imagem.
        Assert.Empty(Ids(Pc.Healthy() with { Memory = null, Cpu = null, Gpus = [] }));
    }

    [Fact]
    public void Notebook_pergunta_bateria_e_nunca_passa_de_tres_perguntas()
    {
        var laptop = Pc.Laptop(onAc: true);
        var ids = Ids(laptop).ToList();
        Assert.Contains(Onboarding.PowerQuestion, ids);
        Assert.True(ids.Count + 1 <= 3);
        foreach (var (_, s) in Pc.Matrix().Select(m => ((string)m[0], (SystemSnapshot)m[1])))
            Assert.True(Onboarding.Contextual(s, new UserPreferences()).Count + 1 <= 3);
    }

    [Fact]
    public void Nunca_pergunta_o_que_o_scan_detecta()
    {
        var all = Onboarding.Contextual(Pc.Laptop(onAc: true), new UserPreferences()).Append(Onboarding.Main);
        foreach (var q in all)
            foreach (var palavra in new[] { "GPU", "placa", "RAM", "memória", "resolução", "Windows", "processador" })
                Assert.DoesNotContain(palavra, q.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Respostas_viram_preferencia_e_valor_desconhecido_e_ignorado()
    {
        var p = new UserPreferences();
        p = Onboarding.Answer(p, Onboarding.GoalQuestion, "MoreFps");
        p = Onboarding.Answer(p, Onboarding.PriorityQuestion, "Quality");
        p = Onboarding.Answer(p, Onboarding.PowerQuestion, "Battery");
        Assert.Equal(new UserPreferences { Goal = Goal.MoreFps, Graphics = GraphicsPreference.Quality, Power = PowerPreference.Battery }, p);
        Assert.Equal(p, Onboarding.Answer(p, Onboarding.GoalQuestion, "rm -rf"));
        Assert.Equal(p, Onboarding.Answer(p, "outra", "MoreFps"));
    }
}
