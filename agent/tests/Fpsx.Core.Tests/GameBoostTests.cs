using Fpsx.Core.Diagnostics;
using Fpsx.Core.Engine;
using Fpsx.Core.Games;
using Fpsx.Core.Model;
using Fpsx.Core.Optimizations;

namespace Fpsx.Core.Tests;

public class HardwareTierTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static SystemSnapshot Pc(double ramGb, int threads, params GpuInfo[] gpus) => Tests.Pc.Healthy() with
    {
        Memory = new MemoryInfo { TotalBytes = (long)(ramGb * Gb), AvailableBytes = Gb, PagefilePresent = true },
        Cpu = Tests.Pc.Healthy().Cpu! with { Threads = threads },
        Gpus = gpus,
    };

    private static GpuInfo Dedicated(long vramGb) => new() { Name = "GPU", Vendor = GpuVendor.Nvidia, VramBytes = vramGb * Gb };

    private static readonly GpuInfo Integrated = new() { Name = "Intel UHD", Vendor = GpuVendor.Intel, LikelyIntegrated = true };

    [Fact]
    public void Classifica_pelo_componente_mais_fraco()
    {
        Assert.Equal(HardwareTier.High, HardwareTierClassifier.Assess(Tests.Pc.Healthy()).Tier);
        Assert.Equal(HardwareTier.Low, HardwareTierClassifier.Assess(Tests.Pc.Laptop(onAc: true)).Tier);
        Assert.Equal(HardwareTier.Mid, HardwareTierClassifier.Assess(Pc(16, 8, Dedicated(4))).Tier);
        Assert.Equal(HardwareTier.Low, HardwareTierClassifier.Assess(Pc(4, 12, Dedicated(8))).Tier);
        Assert.Equal(HardwareTier.Low, HardwareTierClassifier.Assess(Pc(16, 4, Dedicated(8))).Tier);
        Assert.Equal(HardwareTier.Low, HardwareTierClassifier.Assess(Pc(16, 12, Dedicated(2))).Tier);
        // 8 GB aparecem como ~7,8 no Windows: continua intermediário, não fraco.
        Assert.Equal(HardwareTier.Mid, HardwareTierClassifier.Assess(Pc(7.8, 8, Dedicated(4))).Tier);
        // Notebook com integrada + dedicada boa não é "só integrado".
        Assert.Equal(HardwareTier.High, HardwareTierClassifier.Assess(Pc(16, 16, Integrated, Dedicated(8))).Tier);
    }

    [Fact]
    public void Sem_leitura_de_hardware_nao_chuta()
    {
        var s = Tests.Pc.Healthy() with { Memory = null, Cpu = null, Gpus = [] };
        Assert.Equal(HardwareTier.Unknown, HardwareTierClassifier.Assess(s).Tier);
        Assert.Equal(Decision.Unknown, new GamePresetOptimization().Evaluate(new EvaluationContext(s, TestData.Games())).Decision);
    }

    [Fact]
    public void Relatorio_mostra_o_nivel_e_o_motivo()
    {
        // Pelo DecisionEngine, e nao chamando o diagnostico direto: o scan so'
        // roda diagnostico listado no catalogo, e foi assim que ele sumiu no
        // primeiro scan real.
        var scan = TestData.Engine().Evaluate(Tests.Pc.Laptop(onAc: true), "gaming", "free");
        var finding = scan.Findings.Single(f => f.DiagnosticId == "hardware-tier");
        Assert.Equal("LOW", finding.Evidence["nivel"]);
        Assert.Contains("integrado", finding.Detail);
    }

    [Fact]
    public void Todo_diagnostico_compilado_esta_no_catalogo()
    {
        var listed = TestData.Catalog().Diagnostics.Select(d => d.Id).ToHashSet();
        Assert.All(DiagnosticRegistry.All, d => Assert.Contains(d.Id, listed));
    }
}

public class GamePresetTests
{
    private const long Gb = 1024L * 1024 * 1024;

    /// <summary>Notebook de entrada (só vídeo integrado) com os três jogos abertos uma vez.</summary>
    public static SystemSnapshot WeakWithGames() => Pc.Laptop(onAc: true) with
    {
        Games =
        [
            new GameInstall
            {
                GameId = "cs2", Name = "Counter-Strike 2", InstallPath = @"C:\Steam\steamapps\common\cs2", ConfigPath = "cs2_video.txt",
                Config = new Dictionary<string, string>
                {
                    ["setting.mat_vsync"] = "0", ["setting.msaa_samples"] = "4", ["setting.videocfg_shadow_quality"] = "2",
                    ["setting.videocfg_texture_detail"] = "0", ["setting.shaderquality"] = "1",
                },
            },
            new GameInstall
            {
                GameId = "fortnite", Name = "Fortnite", ConfigPath = "GameUserSettings.ini",
                Config = new Dictionary<string, string>
                {
                    ["/Script/FortniteGame.FortGameUserSettings|bUseVSync"] = "True",
                    ["ScalabilityGroups|sg.ShadowQuality"] = "3", ["ScalabilityGroups|sg.ViewDistanceQuality"] = "0",
                },
            },
            new GameInstall
            {
                GameId = "minecraft", Name = "Minecraft Java", ConfigPath = "options.txt",
                Config = new Dictionary<string, string>
                {
                    ["renderDistance"] = "4", ["particles"] = "0", ["renderClouds"] = "\"true\"", ["graphicsMode"] = "1",
                },
            },
        ],
    };

    private static Evaluation Eval(SystemSnapshot s) => new GamePresetOptimization().Evaluate(new EvaluationContext(s, TestData.Games()));

    [Fact]
    public void PC_fraco_recebe_o_preset_de_cada_jogo_so_nas_opcoes_mais_pesadas()
    {
        var e = Eval(WeakWithGames());
        Assert.Equal(Decision.Recommended, e.Decision);

        var cs2 = e.Proposals.Single(p => p.Id == "game-preset-low-end:cs2:pc-fraco").Changes.Cast<GameConfigChange>().ToDictionary(c => c.Key, c => c.Value);
        Assert.Equal(new Dictionary<string, string>
        {
            ["setting.msaa_samples"] = "0", ["setting.videocfg_shadow_quality"] = "0", ["setting.shaderquality"] = "0",
        }, cs2);

        // Distância de visão 0 já é mais leve que o preset (1): não sobe.
        var fortnite = e.Proposals.Single(p => p.Id == "game-preset-low-end:fortnite:pc-fraco").Changes.Cast<GameConfigChange>().Select(c => c.Key);
        Assert.Equal(["ScalabilityGroups|sg.ShadowQuality"], fortnite);

        // Distância 4 é menor que 8: fica. Partículas 0 (todas) vão para 1 (reduzidas).
        var mc = e.Proposals.Single(p => p.Id == "game-preset-low-end:minecraft:pc-fraco").Changes.Cast<GameConfigChange>().ToDictionary(c => c.Key, c => c.Value);
        Assert.Equal(new Dictionary<string, string> { ["particles"] = "1", ["renderClouds"] = "\"false\"", ["graphicsMode"] = "0" }, mc);
    }

    [Fact]
    public void PC_forte_nao_recebe_preset_e_intermediario_recebe_o_equilibrado()
    {
        var weak = WeakWithGames();
        Assert.Equal(Decision.NotApplicable, Eval(weak with { Gpus = Pc.Healthy().Gpus, Memory = Pc.Healthy().Memory }).Decision);
        var mid = Eval(weak with { Gpus = [new GpuInfo { Name = "GTX 1650", Vendor = GpuVendor.Nvidia, VramBytes = 4 * Gb }], Memory = Pc.Healthy().Memory });
        Assert.Equal(Decision.Optional, mid.Decision);
        Assert.All(mid.Proposals, p => Assert.EndsWith(":equilibrado", p.Id));
    }

    [Fact]
    public void Jogo_ja_leve_fica_como_esta()
    {
        var s = Pc.Laptop(onAc: true) with
        {
            Games = [new GameInstall { GameId = "minecraft", Name = "Minecraft Java", Config = new Dictionary<string, string> { ["renderDistance"] = "6", ["particles"] = "2" } }],
        };
        Assert.Equal(Decision.AlreadyOptimal, Eval(s).Decision);
    }

    [Theory]
    [InlineData("renderDistance", "12", true)]
    [InlineData("renderDistance", "8", false)]
    [InlineData("renderDistance", "4", false)]
    [InlineData("particles", "0", true)]
    [InlineData("particles", "2", false)]
    [InlineData("entityShadows", "true", true)]
    [InlineData("entityShadows", "false", false)]
    public void Preset_so_reduz(string key, string current, bool applies)
    {
        var preset = TestData.Games().Single(g => g.Id == "minecraft").Presets.Single(p => p.Id == "pc-fraco");
        Assert.Equal(applies, preset.ShouldApply(key, current));
    }

    [Fact]
    public void Preset_aplica_com_o_jogo_fechado_e_desfaz()
    {
        var sys = new FakeSystem();
        foreach (var (k, v) in WeakWithGames().Games.Single(g => g.GameId == "cs2").Config)
            sys.GameConfig[k] = v;
        var store = new SessionStore(TestData.TempDir());
        var scan = TestData.Engine().Evaluate(WeakWithGames(), "low-end", "pro");

        var session = new OptimizationEngine(sys, store).Apply(scan, ["game-preset-low-end:cs2:pc-fraco"], new ApplyOptions());
        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.Equal("0", sys.GameConfig["setting.msaa_samples"]);

        new RollbackManager(sys, store).RollbackSession(session.Id, force: false);
        Assert.Equal("4", sys.GameConfig["setting.msaa_samples"]);
        Assert.Equal("2", sys.GameConfig["setting.videocfg_shadow_quality"]);
    }

    [Fact]
    public void Preset_nao_entra_na_selecao_automatica()
    {
        // Muda a aparência do jogo: só com a pessoa escolhendo.
        var scan = TestData.Engine().Evaluate(WeakWithGames(), "low-end", "ultimate");
        var preset = scan.Optimizations.Single(o => o.Definition.Id == "game-preset-low-end");
        Assert.True(preset.Definition.RequiresUserConfirmation);
        Assert.False(scan.Optimizations.Single(o => o.Definition.Id == "game-preset-low-end").AutoSelected);
    }

    [Fact]
    public void Preset_exige_plano_pro()
    {
        var scan = TestData.Engine().Evaluate(WeakWithGames(), "low-end", "starter");
        Assert.Equal(Decision.Blocked, scan.Optimizations.Single(o => o.Definition.Id == "game-preset-low-end").Decision);
    }

    [Fact]
    public void Toda_chave_de_perfil_que_o_FPSX_grava_passa_na_politica()
    {
        // Perfil JSON e whitelist compilada andam juntos: chave nova num perfil
        // sem regra na SafetyPolicy falharia só no PC do cliente.
        foreach (var profile in TestData.Games())
        {
            foreach (var preset in profile.Presets)
                foreach (var (key, value) in preset.Settings)
                    SafetyPolicy.Validate(new GameConfigChange(profile.Id, key, value));
            foreach (var check in profile.SettingChecks.Where(c => c.FixValue is not null))
                SafetyPolicy.Validate(new GameConfigChange(profile.Id, check.Key, check.FixValue!));
        }
    }

    [Theory]
    [InlineData("minecraft", "renderDistance", "64")]
    [InlineData("minecraft", "renderClouds", "false")]
    [InlineData("minecraft", "resourcePacks", "[]")]
    [InlineData("fortnite", "ScalabilityGroups|sg.ShadowQuality", "9")]
    [InlineData("fortnite", "ScalabilityGroups|sg.ResolutionQuality", "50")]
    [InlineData("cs2", "setting.msaa_samples", "3")]
    public void Politica_recusa_valor_ou_chave_fora_do_formato(string game, string key, string value)
    {
        Assert.Throws<SafetyViolationException>(() => SafetyPolicy.Validate(new GameConfigChange(game, key, value)));
    }

    [Fact]
    public void Perfis_novos_carregam_com_fonte_e_formato_conhecidos()
    {
        var games = TestData.Games().ToDictionary(g => g.Id);
        Assert.Equal(("local_appdata", "ini"), (games["fortnite"].Config!.Source, games["fortnite"].Config!.Format));
        Assert.Equal(("roaming_appdata", "colon_kv"), (games["minecraft"].Config!.Source, games["minecraft"].Config!.Format));
        Assert.True(games["fortnite"].Detect.ByConfigFile);
        Assert.Equal("game/bin/win64/cs2.exe", games["cs2"].Detect.Executable);
    }
}

public class ConfigFileFormatTests
{
    [Fact]
    public void Ini_troca_so_a_chave_da_secao_certa_e_preserva_o_resto()
    {
        const string text = "[ScalabilityGroups]\r\nsg.ShadowQuality=3\r\nsg.TextureQuality=2\r\n\r\n[Outra]\r\nsg.ShadowQuality=9\r\n";
        var updated = ConfigFiles.Replace(ConfigFiles.Ini, text, "ScalabilityGroups|sg.ShadowQuality", "0")!;
        Assert.Equal(text.Replace("sg.ShadowQuality=3", "sg.ShadowQuality=0"), updated);
        Assert.Equal("9", ConfigFiles.Parse(ConfigFiles.Ini, updated)["Outra|sg.ShadowQuality"]);
        Assert.Null(ConfigFiles.Replace(ConfigFiles.Ini, text, "ScalabilityGroups|sg.NaoExiste", "0"));
    }

    [Fact]
    public void ChaveValor_do_Minecraft_preserva_aspas_e_outras_linhas()
    {
        const string text = "version:3955\nrenderClouds:\"true\"\nrenderDistance:12\nlastServer:mc.exemplo.com:25565\n";
        var updated = ConfigFiles.Replace(ConfigFiles.ColonKv, text, "renderClouds", "\"false\"")!;
        Assert.Equal(text.Replace("renderClouds:\"true\"", "renderClouds:\"false\""), updated);
        // Só o primeiro ":" separa: o valor pode ter ":" dentro.
        Assert.Equal("mc.exemplo.com:25565", ConfigFiles.Parse(ConfigFiles.ColonKv, text)["lastServer"]);
    }
}

public class WindowsGameSettingsTests
{
    private static readonly GpuInfo Integrated = new() { Name = "Intel UHD", Vendor = GpuVendor.Intel, LikelyIntegrated = true };
    private static readonly GpuInfo Dedicated = new() { Name = "RTX 3050 Laptop", Vendor = GpuVendor.Nvidia, VramBytes = 4L << 30 };
    private const string Exe = @"C:\Steam\steamapps\common\cs2\game\bin\win64\cs2.exe";

    private static SystemSnapshot HybridLaptop(string? currentPreference = null) => Pc.Laptop(onAc: false) with
    {
        Gpus = [Integrated, Dedicated],
        Games = [new GameInstall { GameId = "cs2", Name = "Counter-Strike 2", InstallPath = @"C:\Steam\steamapps\common\cs2", ExecutablePath = Exe }],
        Gaming = new GamingFeatures
        {
            GpuPreferences = currentPreference is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [Exe] = currentPreference },
        },
    };

    private static Evaluation Eval(IOptimization o, SystemSnapshot s) => o.Evaluate(new EvaluationContext(s, TestData.Games()));

    [Fact]
    public void Notebook_com_duas_GPUs_manda_o_jogo_para_a_dedicada()
    {
        var e = Eval(new GameGpuPreferenceOptimization(), HybridLaptop());
        Assert.Equal(Decision.Recommended, e.Decision);
        var change = (RegistryValueChange)e.Proposals.Single().Changes.Single();
        Assert.Equal((Exe, "GpuPreference=2;"), (change.Name, change.Value!.Data));
        Assert.Contains("bateria", e.Warning);
        SafetyPolicy.Validate(change);
    }

    [Fact]
    public void Preferencia_existente_preserva_os_outros_pares()
    {
        var change = (RegistryValueChange)Eval(new GameGpuPreferenceOptimization(), HybridLaptop("SwapEffectUpgradeEnable=1;GpuPreference=1;")).Proposals.Single().Changes.Single();
        Assert.Equal("SwapEffectUpgradeEnable=1;GpuPreference=2;", change.Value!.Data);
    }

    [Fact]
    public void Uma_GPU_so_ou_ja_configurado_nao_oferece_nada()
    {
        Assert.Equal(Decision.NotApplicable, Eval(new GameGpuPreferenceOptimization(), HybridLaptop() with { Gpus = [Dedicated] }).Decision);
        Assert.Equal(Decision.AlreadyOptimal, Eval(new GameGpuPreferenceOptimization(), HybridLaptop("GpuPreference=2;")).Decision);
        Assert.Equal(Decision.NotApplicable, Eval(new GameGpuPreferenceOptimization(), Pc.Healthy()).Decision);
    }

    [Fact]
    public void Preferencia_de_GPU_aplica_e_desfaz_apagando_o_valor()
    {
        var sys = new FakeSystem();
        var store = new SessionStore(TestData.TempDir());
        var scan = TestData.Engine().Evaluate(HybridLaptop(), "gaming", "starter");
        var session = new OptimizationEngine(sys, store).Apply(scan, ["game-gpu-high-performance"], new ApplyOptions());
        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.Equal("GpuPreference=2;", sys.ReadRegistry(RegistryRoot.CurrentUser, RegistryPaths.DirectXUserGpuPreferences, Exe)!.Data);

        new RollbackManager(sys, store).RollbackSession(session.Id, force: false);
        Assert.Null(sys.ReadRegistry(RegistryRoot.CurrentUser, RegistryPaths.DirectXUserGpuPreferences, Exe));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\evil.dll", "GpuPreference=2;")]
    [InlineData(@"cs2.exe", "GpuPreference=2;")]
    [InlineData(Exe, "GpuPreference=2;Debug=1;")]
    [InlineData(Exe, "GpuPreference=2")]
    [InlineData("OutroValor", "SwapEffectUpgradeEnable=1;")]
    public void Politica_so_aceita_preferencia_de_GPU_no_formato_do_Windows(string name, string data)
    {
        var change = new RegistryValueChange(RegistryRoot.CurrentUser, RegistryPaths.DirectXUserGpuPreferences, name, new RegValue(RegistryKind.String, data));
        Assert.Throws<SafetyViolationException>(() => SafetyPolicy.Validate(change));
        Assert.Throws<SafetyViolationException>(() => SafetyPolicy.Validate(change with { Root = RegistryRoot.LocalMachine }));
    }

    [Theory]
    [InlineData(22631, null, Decision.Recommended)]
    [InlineData(22631, "SwapEffectUpgradeEnable=0;", Decision.Optional)]
    [InlineData(26100, null, Decision.AlreadyOptimal)]
    [InlineData(26100, "VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;", Decision.AlreadyOptimal)]
    [InlineData(19045, null, Decision.NotApplicable)]
    public void Jogos_em_janela_segue_a_versao_e_o_valor_atual(int build, string? raw, Decision expected)
    {
        var s = Pc.Healthy() with { Os = new OsInfo { Build = build }, Gaming = new GamingFeatures { DirectXGlobalSettings = raw } };
        var e = Eval(new WindowedGamesOptimization(), s);
        Assert.Equal(expected, e.Decision);
        foreach (var change in e.Proposals.SelectMany(p => p.Changes))
            SafetyPolicy.Validate(change);
    }

    [Fact]
    public void Jogos_em_janela_preserva_os_outros_pares_globais()
    {
        var s = Pc.Healthy() with { Os = new OsInfo { Build = 22631 }, Gaming = new GamingFeatures { DirectXGlobalSettings = "VRROptimizeEnable=0;SwapEffectUpgradeEnable=0;" } };
        var change = (RegistryValueChange)Eval(new WindowedGamesOptimization(), s).Proposals.Single().Changes.Single();
        Assert.Equal("VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;", change.Value!.Data);
    }

    [Fact]
    public void Transparencia_so_em_PC_de_entrada()
    {
        var weak = Pc.Laptop(onAc: true);
        var e = Eval(new TransparencyOptimization(), weak);
        Assert.Equal(Decision.Optional, e.Decision);
        SafetyPolicy.Validate(e.Proposals.Single().Changes.Single());

        Assert.Equal(Decision.AlreadyOptimal, Eval(new TransparencyOptimization(), weak with { Gaming = new GamingFeatures { TransparencyValue = 0 } }).Decision);
        Assert.Equal(Decision.NotApplicable, Eval(new TransparencyOptimization(), Pc.Healthy()).Decision);

        var bad = new RegistryValueChange(RegistryRoot.CurrentUser, RegistryPaths.Personalize, "EnableTransparency", RegValue.DWord(2));
        Assert.Throws<SafetyViolationException>(() => SafetyPolicy.Validate(bad));
        Assert.Throws<SafetyViolationException>(() => SafetyPolicy.Validate(bad with { Name = "AppsUseLightTheme", Value = RegValue.DWord(0) }));
    }

    [Fact]
    public void PC_forte_e_bem_configurado_nao_recebe_nada_novo()
    {
        var scan = TestData.Engine().Evaluate(Pc.Healthy(), "gaming", "ultimate");
        foreach (var id in new[] { "game-preset-low-end", "game-gpu-high-performance", "windowed-game-optimizations", "windows-transparency-off" })
            Assert.DoesNotContain(scan.Optimizations.Single(o => o.Definition.Id == id).Decision, new[] { Decision.Recommended, Decision.Optional });
    }
}
