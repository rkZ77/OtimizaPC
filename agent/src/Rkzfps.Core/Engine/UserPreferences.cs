using Rkzfps.Core.Diagnostics;
using Rkzfps.Core.Model;

namespace Rkzfps.Core.Engine;

// Preferência é o que o computador não consegue descobrir sozinho: o que a
// pessoa quer melhorar e quanto de imagem aceita trocar. Hardware, Windows,
// monitor e jogos vêm do scan, nunca de pergunta. Tudo fica só no PC.

/// <summary>"O que você quer melhorar?" (pergunta principal).</summary>
public enum Goal
{
    MoreFps,
    LessStutter,
    LowerLatency,
    Balanced,
}

/// <summary>Quanto de imagem a pessoa aceita trocar por desempenho.</summary>
public enum GraphicsPreference
{
    Performance,
    Balanced,
    Quality,
}

/// <summary>Só em notebook: desempenho ou bateria.</summary>
public enum PowerPreference
{
    Performance,
    Battery,
}

/// <summary>Os perfis da tela. Cada um aponta para um perfil interno do catálogo (profiles.json).</summary>
public enum ProfileChoice
{
    Automatic,
    Performance,
    Balanced,
    Quality,
    Custom,

    /// <summary>Um dos perfis internos escolhido direto na área avançada (ex.: Streaming).</summary>
    Advanced,
}

public sealed record UserPreferences
{
    public ProfileChoice Choice { get; init; } = ProfileChoice.Automatic;

    /// <summary>null = pulou: o resolvedor decide pelo hardware.</summary>
    public Goal? Goal { get; init; }

    public GraphicsPreference? Graphics { get; init; }
    public PowerPreference? Power { get; init; }

    public static string Label(ProfileChoice c) => c switch
    {
        ProfileChoice.Automatic => "Automático",
        ProfileChoice.Performance => "Desempenho",
        ProfileChoice.Balanced => "Equilibrado",
        ProfileChoice.Quality => "Qualidade",
        ProfileChoice.Custom => "Personalizado",
        _ => "Avançado",
    };

    public static string Description(ProfileChoice c) => c switch
    {
        ProfileChoice.Automatic => "O RKZFPS decide pelo seu hardware e pelos seus jogos.",
        ProfileChoice.Performance => "Prioriza FPS e menor atraso.",
        ProfileChoice.Balanced => "Equilibra FPS, imagem e estabilidade.",
        ProfileChoice.Quality => "Mantém a imagem do jogo e corrige só o que atrapalha.",
        ProfileChoice.Custom => "Nada é marcado sozinho: você escolhe cada item.",
        _ => "Perfil escolhido na área avançada.",
    };

    /// <summary>
    /// Quem já usava o app antes das perguntas tem só o id interno salvo. Ele
    /// aparece na tela como o perfil novo equivalente, sem mudar nada no que
    /// é aplicado: os que não têm equivalente ficam na área avançada.
    /// </summary>
    public static ProfileChoice FromLegacy(string catalogProfileId) => catalogProfileId.ToLowerInvariant() switch
    {
        "gaming" => ProfileChoice.Balanced,
        "competitive" => ProfileChoice.Performance,
        "high-end" => ProfileChoice.Quality,
        "custom" => ProfileChoice.Custom,
        _ => ProfileChoice.Advanced,
    };
}

public sealed record OnboardingOption(string Label, string Value);

public sealed record OnboardingQuestion(string Id, string Title, IReadOnlyList<OnboardingOption> Options);

/// <summary>
/// As perguntas da primeira abertura: no máximo três, todas puláveis, e só as
/// que mudam alguma decisão neste PC. Sem snapshot (scan ainda rodando) sai
/// só a principal; as de contexto entram quando o hardware for lido.
/// </summary>
public static class Onboarding
{
    public const string GoalQuestion = "goal";
    public const string GraphicsQuestion = "graphics";
    public const string PriorityQuestion = "priority";
    public const string PowerQuestion = "power";

    public static OnboardingQuestion Main { get; } = new(GoalQuestion, "O que você quer melhorar?",
    [
        new("Mais FPS", nameof(Engine.Goal.MoreFps)),
        new("Menos travadas", nameof(Engine.Goal.LessStutter)),
        new("Menor latência", nameof(Engine.Goal.LowerLatency)),
        new("Equilíbrio", nameof(Engine.Goal.Balanced)),
    ]);

    /// <summary>Monitor a partir desta taxa é de quem joga competitivo: a pergunta vira FPS ou imagem.</summary>
    public const int HighRefreshHz = 144;

    /// <summary>Perguntas de contexto para este PC, depois da principal. Pode ser vazio.</summary>
    public static IReadOnlyList<OnboardingQuestion> Contextual(SystemSnapshot snapshot, UserPreferences prefs)
    {
        var list = new List<OnboardingQuestion>();
        var tier = HardwareTierClassifier.Assess(snapshot).Tier;
        var hz = MaxRefreshHz(snapshot);

        // Quem pediu equilíbrio já respondeu a troca de imagem; em PC sem
        // nível lido a resposta não mudaria nada.
        if (prefs.Goal != Engine.Goal.Balanced && tier != HardwareTier.Unknown)
        {
            if (hz >= HighRefreshHz && tier != HardwareTier.Low)
                list.Add(new(PriorityQuestion, "O que é mais importante?",
                [
                    new("FPS e latência", nameof(GraphicsPreference.Performance)),
                    new("Qualidade gráfica", nameof(GraphicsPreference.Quality)),
                ]));
            // PC forte em monitor comum: o RKZFPS não baixa gráfico nele, então não pergunta.
            else if (tier != HardwareTier.High)
                list.Add(new(GraphicsQuestion, "Você aceita reduzir um pouco a qualidade gráfica para ganhar desempenho?",
                [
                    new("Sim, pode reduzir", nameof(GraphicsPreference.Performance)),
                    new("Não, prefiro a imagem", nameof(GraphicsPreference.Quality)),
                ]));
        }

        if (snapshot.Power?.HasBattery == true)
            list.Add(new(PowerQuestion, "O que você prefere?",
            [
                new("Desempenho", nameof(PowerPreference.Performance)),
                new("Bateria", nameof(PowerPreference.Battery)),
            ]));
        return list;
    }

    /// <summary>Grava a resposta. Valor desconhecido é ignorado: vale o que já estava.</summary>
    public static UserPreferences Answer(UserPreferences prefs, string questionId, string value) => questionId switch
    {
        GoalQuestion when Enum.TryParse<Goal>(value, out var g) => prefs with { Goal = g },
        GraphicsQuestion or PriorityQuestion when Enum.TryParse<GraphicsPreference>(value, out var gp) => prefs with { Graphics = gp },
        PowerQuestion when Enum.TryParse<PowerPreference>(value, out var p) => prefs with { Power = p },
        _ => prefs,
    };

    public static int MaxRefreshHz(SystemSnapshot s)
    {
        var primary = s.Displays.FirstOrDefault(d => d.IsPrimary) ?? s.Displays.FirstOrDefault();
        return primary is null ? 0 : Math.Max(primary.MaxHzAtCurrentResolution, primary.CurrentHz);
    }
}
