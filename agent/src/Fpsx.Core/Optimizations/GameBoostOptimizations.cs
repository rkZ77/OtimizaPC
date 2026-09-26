using Fpsx.Core.Diagnostics;
using Fpsx.Core.Model;

namespace Fpsx.Core.Optimizations;

// Métodos para PC fraco rodar jogo. O que mais pesa, de longe, é a qualidade
// gráfica do próprio jogo: por isso o preset leve é o item principal, e os
// outros são ajustes do Windows que só fazem diferença em cenários
// específicos (notebook com duas GPUs, jogo em janela, vídeo integrado).
// Nenhum deles aparece em PC onde não resolve nada.

/// <summary>
/// Configuração do jogo pelo nível do PC. Entrada: leve (sombras, efeitos,
/// oclusão e antisserrilhado no mínimo), recomendada. Intermediário:
/// equilibrada (baixa só o que costuma pesar sem deixar o jogo feio),
/// opcional. Forte: nenhuma, só as correções de latência do game-settings-fix.
/// Muda a imagem: sempre pede confirmação, e nunca sobe qualidade.
/// </summary>
public sealed class GamePresetOptimization : IOptimization
{
    public string Id => "game-preset-low-end";

    public Evaluation Evaluate(EvaluationContext context)
    {
        var tier = HardwareTierClassifier.Assess(context.Snapshot);
        var evidence = Ev.Of(("nivel", tier.Tier.ToString().ToUpperInvariant()), ("motivo", string.Join(" ", tier.Reasons)));
        if (tier.Tier == HardwareTier.Unknown)
            return Evaluation.Unknown("Não foi possível ler o hardware para decidir se vale reduzir a qualidade gráfica.");
        // PC forte não tem o que ganhar trocando imagem por FPS: nesse caso o
        // FPSX só corrige o que aumenta atraso (V-Sync, Reflex, taxa do monitor).
        if (tier.Tier == HardwareTier.High)
            return Evaluation.NotApplicable($"{tier.Label}: reduzir a qualidade gráfica não é necessário aqui.", evidence);

        var proposals = new List<Proposal>();
        var anyGame = false;
        foreach (var profile in context.GameProfiles)
        {
            var game = context.Game(profile.Id);
            if (game is null || game.Config.Count == 0)
                continue;
            anyGame = true;

            foreach (var preset in profile.Presets.Where(p => p.Tiers.Contains(tier.Tier)))
            {
                // Só chave que já existe no arquivo e que está mais pesada que o
                // preset: o FPSX não inventa chave nem piora o que já está leve.
                var changes = preset.Settings
                    .Where(kv => game.Config.TryGetValue(kv.Key, out var current) && preset.ShouldApply(kv.Key, current))
                    .Select(kv => (Change)new GameConfigChange(profile.Id, kv.Key, kv.Value))
                    .ToList();
                if (changes.Count == 0)
                    continue;
                proposals.Add(new Proposal($"{Id}:{profile.Id}:{preset.Id}",
                    $"{profile.Name}: {preset.Title} ({changes.Count} {(changes.Count == 1 ? "opção" : "opções")})",
                    changes, Potential.High, preset.Description));
            }
        }

        if (!anyGame)
            return Evaluation.NotApplicable("Nenhum jogo com perfil encontrado, ou o jogo ainda não criou a configuração de vídeo.", evidence);
        if (proposals.Count == 0)
            return Evaluation.Optimal("Os jogos encontrados já estão na configuração certa para este PC.", evidence);

        var low = tier.Tier == HardwareTier.Low;
        return new Evaluation
        {
            Decision = low ? Decision.Recommended : Decision.Optional,
            Potential = low ? Potential.High : Potential.Moderate,
            Reason = low
                ? "Neste PC o hardware é o limite. Baixar sombras, efeitos e antisserrilhado é o que mais aumenta o FPS, bem mais do que qualquer ajuste do Windows."
                : "Este PC roda bem, mas algumas opções custam muito FPS e mudam pouco a imagem. A configuração equilibrada baixa só essas.",
            Warning = "Muda a aparência do jogo: sombras, efeitos e texturas ficam mais simples. Feche o jogo antes de aplicar. Tudo volta com Desfazer. Meça com o FPSX Benchmark antes e depois.",
            Evidence = evidence,
            Proposals = proposals,
        };
    }
}

/// <summary>
/// Notebook (ou desktop) com vídeo integrado E placa dedicada: garante que o
/// jogo rode na dedicada. É a mesma opção de Configurações > Tela > Gráficos,
/// gravada só para os executáveis de jogos detectados.
/// </summary>
public sealed class GameGpuPreferenceOptimization : IOptimization
{
    public string Id => "game-gpu-high-performance";

    public const string HighPerformance = "2";

    public Evaluation Evaluate(EvaluationContext context)
    {
        var s = context.Snapshot;
        var hybrid = s.Gpus.Any(g => g.LikelyIntegrated) && s.Gpus.Any(g => !g.LikelyIntegrated);
        var evidence = Ev.Of(("gpus", string.Join(" + ", s.Gpus.Select(g => g.Name))));
        if (!hybrid)
            return Evaluation.NotApplicable("Este PC tem só uma placa de vídeo ativa: o Windows não tem outra para escolher.", evidence);

        var games = s.Games.Where(g => !string.IsNullOrEmpty(g.ExecutablePath)).ToList();
        if (games.Count == 0)
            return Evaluation.NotApplicable("Nenhum jogo com perfil e executável conhecido foi encontrado.", evidence);

        var proposals = new List<Proposal>();
        foreach (var game in games)
        {
            var raw = s.Gaming.GpuPreferences.GetValueOrDefault(game.ExecutablePath!);
            if (DirectXSettings.Parse(raw).GetValueOrDefault("GpuPreference") == HighPerformance)
                continue;
            var label = $"Rodar o {game.Name} na placa de vídeo dedicada";
            proposals.Add(new Proposal($"{Id}:{game.GameId}", label,
                [new RegistryValueChange(RegistryRoot.CurrentUser, RegistryPaths.DirectXUserGpuPreferences, game.ExecutablePath!,
                    new RegValue(RegistryKind.String, DirectXSettings.With(raw, "GpuPreference", HighPerformance)), Label: label)],
                Potential.High, "Com duas GPUs, o Windows pode escolher a integrada para o jogo."));
        }

        if (proposals.Count == 0)
            return Evaluation.Optimal("Os jogos encontrados já estão configurados para a placa dedicada.", evidence);

        return new Evaluation
        {
            Decision = Decision.Recommended,
            Potential = Potential.High,
            Reason = "Este PC tem vídeo integrado e placa dedicada. Sem preferência definida, o Windows pode rodar o jogo no integrado, que é bem mais fraco. É comum em notebook.",
            Warning = s.Power?.HasBattery == true ? "Em notebook, a placa dedicada gasta mais bateria enquanto o jogo está aberto." : null,
            Evidence = evidence,
            Proposals = proposals,
        };
    }
}

/// <summary>
/// "Otimizações para jogos em janela" do Windows 11: jogos DirectX 10/11 em
/// janela ou tela cheia sem borda passam a usar o modelo de apresentação
/// moderno, com menos atraso. Não mexe em tela cheia exclusiva.
/// </summary>
public sealed class WindowedGamesOptimization : IOptimization
{
    public string Id => "windowed-game-optimizations";

    private const string Key = "SwapEffectUpgradeEnable";

    public Evaluation Evaluate(EvaluationContext context)
    {
        var s = context.Snapshot;
        var raw = s.Gaming.DirectXGlobalSettings;
        var current = DirectXSettings.Parse(raw).GetValueOrDefault(Key);
        var evidence = Ev.Of((Key, current ?? "ausente"), ("build", s.Os.Build.ToString()));

        // A opção aparece no Windows 11 22H2 (build 22621).
        if (s.Os.Build is > 0 and < 22621)
            return Evaluation.NotApplicable("Recurso do Windows 11 22H2 em diante.", evidence);
        if (current == "1")
            return Evaluation.Optimal("A otimização para jogos em janela já está ligada.", evidence);
        // No 24H2 o recurso vem ligado quando ninguém mexeu no valor.
        if (current is null && s.Os.Build >= 26100)
            return Evaluation.Optimal("No Windows 11 24H2 a otimização para jogos em janela já vem ligada.", evidence);

        return new Evaluation
        {
            // Desligado de propósito pode ter sido para contornar um jogo com
            // problema: aí fica como opção, não como recomendação.
            Decision = current == "0" ? Decision.Optional : Decision.Recommended,
            Potential = Potential.Moderate,
            Reason = "Jogos DirectX 10 e 11 em janela ou tela cheia sem borda passam a usar o modo de apresentação moderno do Windows, com menos atraso entre o quadro pronto e a tela.",
            Warning = current == "0" ? "Alguém desligou esta opção. Se foi por causa de um jogo específico, ele pode voltar a ter o mesmo problema." : null,
            Evidence = evidence,
            Proposals =
            [
                new Proposal(Id, "Ativar a otimização para jogos em janela",
                    [new RegistryValueChange(RegistryRoot.CurrentUser, RegistryPaths.DirectXUserGpuPreferences, DirectXSettings.GlobalValueName,
                        new RegValue(RegistryKind.String, DirectXSettings.With(raw, Key, "1")), Label: "Ativar a otimização para jogos em janela do Windows")],
                    Potential.Moderate, "Mesma opção de Configurações > Sistema > Tela > Gráficos."),
            ],
        };
    }
}

/// <summary>
/// Efeitos de transparência (vidro da barra de tarefas e dos menus). Só em PC
/// de entrada, onde o vídeo integrado divide a GPU com o jogo. O ganho é
/// pequeno e aparece em janela e na fluidez do Windows, não em tela cheia.
/// </summary>
public sealed class TransparencyOptimization : IOptimization
{
    public string Id => "windows-transparency-off";

    public Evaluation Evaluate(EvaluationContext context)
    {
        var value = context.Snapshot.Gaming.TransparencyValue;
        var tier = HardwareTierClassifier.Assess(context.Snapshot);
        var evidence = Ev.Of(("EnableTransparency", value?.ToString() ?? "ausente (padrão: ligado)"), ("nivel", tier.Tier.ToString().ToUpperInvariant()));

        if (tier.Tier != HardwareTier.Low)
            return Evaluation.NotApplicable("Fora de PC de entrada, o efeito de transparência não pesa de forma perceptível.", evidence);
        if (value == 0)
            return Evaluation.Optimal("Os efeitos de transparência já estão desligados.", evidence);

        return new Evaluation
        {
            Decision = Decision.Optional,
            Potential = Potential.Low,
            Reason = "Neste PC o vídeo é fraco, e o efeito de vidro do Windows usa a mesma GPU do jogo. Desligar alivia jogo em janela e deixa o Windows mais leve.",
            Warning = "Muda só a aparência do Windows: barra de tarefas e menus ficam opacos.",
            Evidence = evidence,
            Proposals =
            [
                new Proposal(Id, "Desligar os efeitos de transparência do Windows",
                    [new RegistryValueChange(RegistryRoot.CurrentUser, RegistryPaths.Personalize, "EnableTransparency", RegValue.DWord(0), Label: "Desligar os efeitos de transparência do Windows")],
                    Potential.Low, "Mesma opção de Configurações > Personalização > Cores."),
            ],
        };
    }
}
