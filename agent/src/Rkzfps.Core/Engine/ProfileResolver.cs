using Rkzfps.Core.Diagnostics;
using Rkzfps.Core.Model;

namespace Rkzfps.Core.Engine;

public enum Objective
{
    MaximumPerformance,
    Smoothness,
    LowLatency,
    Balanced,
    Quality,
}

public enum Aggressiveness
{
    Minimal,
    Moderate,
    High,
}

/// <summary>Quanto de imagem do jogo pode ser trocado por FPS neste PC, para esta pessoa.</summary>
public enum GraphicsTradeoff
{
    None,
    Light,
    Medium,
}

public enum Priority
{
    Low,
    Medium,
    High,
}

/// <summary>
/// O que o RKZFPS entendeu: preferência mais hardware. Não executa nada.
/// Quem decide e aplica continua sendo o DecisionEngine com a SafetyPolicy;
/// este perfil só escolhe o perfil interno do catálogo e ajusta, entre
/// Recomendada e Opcional, o que o motor já tinha liberado.
/// </summary>
public sealed record OptimizationProfile
{
    /// <summary>Perfil interno de profiles.json que vai para o DecisionEngine.</summary>
    public string CatalogProfileId { get; init; } = "gaming";

    /// <summary>Usuário antigo que nunca respondeu: comportamento exatamente o de antes.</summary>
    public bool Legacy { get; init; }

    public ProfileChoice Choice { get; init; }
    public Objective Objective { get; init; } = Objective.Balanced;
    public Aggressiveness Aggressiveness { get; init; } = Aggressiveness.Moderate;
    public GraphicsTradeoff GraphicsTradeoff { get; init; } = GraphicsTradeoff.None;
    public Priority LatencyPriority { get; init; } = Priority.Medium;
    public Priority StabilityPriority { get; init; } = Priority.Medium;
    public Priority TemperaturePriority { get; init; } = Priority.Medium;
    public bool PreferBattery { get; init; }

    /// <summary>Seleção automática ligada (Personalizado desliga: a pessoa marca item a item).</summary>
    public bool Automation { get; init; } = true;

    public HardwareTier Tier { get; init; }
    public int RefreshHz { get; init; }

    /// <summary>Frase curta para a pessoa: o que o RKZFPS vai priorizar.</summary>
    public string Summary { get; init; } = "";

    /// <summary>Por que cada decisão, em palavras simples (tela e contexto da IA).</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];

    /// <summary>
    /// Ajusta o resultado do motor à preferência. Só move itens entre
    /// Recomendada e Opcional: nunca libera bloqueado (plano, admin), nunca
    /// cria proposta e nunca marca seleção automática. Item que muda imagem
    /// continua pedindo confirmação, porque isso vem do catálogo.
    /// </summary>
    public ScanResult ApplyTo(ScanResult scan) =>
        Legacy ? scan : scan with { Optimizations = scan.Optimizations.Select(Adjust).ToList() };

    private const string Preset = "game-preset-low-end";
    private static readonly string[] PowerIds = ["power-plan-high-performance", "power-plan-leave-power-saver"];

    private OptimizationResult Adjust(OptimizationResult r)
    {
        var id = r.Definition.Id;
        if (id == Preset)
        {
            if (GraphicsTradeoff == GraphicsTradeoff.None)
                return Demote(r, "Fica como opção: você pediu para manter a qualidade de imagem.");
            if (Aggressiveness == Aggressiveness.High)
                return Promote(r, "Recomendada porque você aceitou trocar um pouco de imagem por desempenho.");
        }
        else if (PreferBattery && PowerIds.Contains(id))
        {
            return Demote(r, "Fica como opção: você prefere poupar bateria no notebook.");
        }
        return r;
    }

    // O motor mostra a decisão final em Decision e a necessidade em
    // Evaluation.Decision (é por ela que o Free vê o que melhoraria). As duas
    // andam juntas, mas Decision bloqueada pelo plano continua bloqueada.
    private static OptimizationResult Demote(OptimizationResult r, string note)
    {
        if (r.Evaluation.Decision != Decision.Recommended && r.Decision != Decision.Recommended)
            return r;
        return r with
        {
            Evaluation = r.Evaluation.Decision == Decision.Recommended ? r.Evaluation with { Decision = Decision.Optional } : r.Evaluation,
            Decision = r.Decision == Decision.Recommended ? Decision.Optional : r.Decision,
            AutoSelected = false,
            Reason = $"{r.Reason} {note}".Trim(),
        };
    }

    private static OptimizationResult Promote(OptimizationResult r, string note)
    {
        if (r.Evaluation.Decision != Decision.Optional)
            return r;
        return r with
        {
            Evaluation = r.Evaluation with { Decision = Decision.Recommended },
            Decision = r.Decision == Decision.Optional ? Decision.Recommended : r.Decision,
            Reason = $"{r.Reason} {note}".Trim(),
        };
    }
}

/// <summary>
/// O cérebro entre o que a pessoa pediu e o que o PC é. Mesmo pedido, PCs
/// diferentes, decisões diferentes: "Mais FPS" num PC forte não piora a
/// imagem (não há o que ganhar com isso), num PC de entrada traz a
/// configuração leve do jogo. Determinístico e local: nenhuma chamada de IA.
/// </summary>
public static class ProfileResolver
{
    public static OptimizationProfile Resolve(UserPreferences? prefs, SystemSnapshot snapshot, string savedProfileId)
    {
        var tier = HardwareTierClassifier.Assess(snapshot).Tier;
        var hz = Onboarding.MaxRefreshHz(snapshot);
        if (prefs is null)
            return new OptimizationProfile
            {
                CatalogProfileId = savedProfileId, Legacy = true, Choice = UserPreferences.FromLegacy(savedProfileId), Tier = tier, RefreshHz = hz,
            };

        var laptop = snapshot.Power?.HasBattery == true;
        var battery = laptop && prefs.Power == PowerPreference.Battery;

        var graphics = prefs.Choice switch
        {
            ProfileChoice.Performance => GraphicsPreference.Performance,
            ProfileChoice.Quality => GraphicsPreference.Quality,
            ProfileChoice.Balanced => GraphicsPreference.Balanced,
            _ => prefs.Graphics ?? (prefs.Goal == Goal.MoreFps ? GraphicsPreference.Performance : GraphicsPreference.Balanced),
        };

        // Imagem só é moeda de troca onde o hardware é o limite. PC forte e
        // nível desconhecido não trocam nada: na dúvida, não piora o jogo.
        var tradeoff = tier switch
        {
            _ when graphics == GraphicsPreference.Quality => GraphicsTradeoff.None,
            HardwareTier.Low => GraphicsTradeoff.Medium,
            HardwareTier.Mid => GraphicsTradeoff.Light,
            _ => GraphicsTradeoff.None,
        };
        var aggressiveness = tradeoff == GraphicsTradeoff.None ? Aggressiveness.Minimal
            : graphics == GraphicsPreference.Performance ? Aggressiveness.High
            : Aggressiveness.Moderate;

        var objective = prefs.Goal switch
        {
            Goal.MoreFps => Objective.MaximumPerformance,
            Goal.LessStutter => Objective.Smoothness,
            Goal.LowerLatency => Objective.LowLatency,
            Goal.Balanced => Objective.Balanced,
            _ => graphics switch
            {
                GraphicsPreference.Performance => Objective.MaximumPerformance,
                GraphicsPreference.Quality => Objective.Quality,
                _ => Objective.Balanced,
            },
        };

        var competitive = hz >= Onboarding.HighRefreshHz;
        var latency = objective == Objective.LowLatency || (objective == Objective.MaximumPerformance && competitive) ? Priority.High
            : objective == Objective.Quality ? Priority.Low
            : Priority.Medium;
        var stability = objective is Objective.Smoothness or Objective.Quality ? Priority.High : Priority.Medium;
        var temperature = !laptop ? Priority.Low : prefs.Power == PowerPreference.Performance ? Priority.Medium : Priority.High;

        var reasons = new List<string>();
        switch (tier)
        {
            case HardwareTier.High:
                reasons.Add("Seu PC é forte: não precisa piorar a imagem do jogo para ganhar FPS. O foco fica em atraso, taxa do monitor e energia.");
                break;
            case HardwareTier.Low when tradeoff != GraphicsTradeoff.None:
                reasons.Add("Seu PC é de entrada: a configuração leve dos jogos é o que mais ajuda. Ela muda a imagem e sempre pede sua confirmação.");
                break;
            case HardwareTier.Mid when aggressiveness == Aggressiveness.High:
                reasons.Add("Seu PC roda bem: a configuração equilibrada dos jogos baixa só o que pesa muito e quase não aparece. Sempre pede sua confirmação.");
                break;
            case HardwareTier.Unknown:
                reasons.Add("Não deu para ler todo o hardware: o RKZFPS não mexe na imagem dos jogos e fica só nas correções seguras.");
                break;
        }
        if (graphics == GraphicsPreference.Quality && tier is HardwareTier.Low or HardwareTier.Mid)
            reasons.Add("Você prefere a qualidade de imagem: a configuração leve dos jogos fica como opção, não como recomendação.");
        if (latency == Priority.High && competitive)
            reasons.Add($"Monitor de {hz} Hz: prioridade para menor atraso e para usar a taxa máxima da tela.");
        if (battery)
            reasons.Add("Notebook com prioridade para bateria: o plano de energia não é trocado sozinho.");

        return new OptimizationProfile
        {
            CatalogProfileId = CatalogProfile(prefs.Choice, tier, savedProfileId),
            Choice = prefs.Choice,
            Objective = objective,
            Aggressiveness = aggressiveness,
            GraphicsTradeoff = tradeoff,
            LatencyPriority = latency,
            StabilityPriority = stability,
            TemperaturePriority = temperature,
            PreferBattery = battery,
            Automation = prefs.Choice != ProfileChoice.Custom,
            Tier = tier,
            RefreshHz = hz,
            Summary = Summary(objective, tradeoff),
            Reasons = reasons,
        };
    }

    /// <summary>Perfil da tela para o perfil interno. Os 7 internos continuam todos existindo.</summary>
    public static string CatalogProfile(ProfileChoice choice, HardwareTier tier, string savedProfileId) => choice switch
    {
        ProfileChoice.Performance => "competitive",
        ProfileChoice.Balanced => "gaming",
        ProfileChoice.Quality => "high-end",
        ProfileChoice.Custom => "custom",
        ProfileChoice.Advanced => savedProfileId,
        _ => tier switch
        {
            HardwareTier.Low => "low-end",
            HardwareTier.High => "high-end",
            _ => "gaming",
        },
    };

    private static string Summary(Objective objective, GraphicsTradeoff tradeoff)
    {
        var sem = tradeoff == GraphicsTradeoff.None ? " sem mexer na imagem dos jogos" : "";
        return objective switch
        {
            Objective.MaximumPerformance => $"Perfeito. Vamos priorizar FPS e baixa latência{sem}, sem aplicar alterações desnecessárias ao seu PC.",
            Objective.Smoothness => $"Perfeito. Vamos priorizar um jogo sem travadas{sem}, sem aplicar alterações desnecessárias ao seu PC.",
            Objective.LowLatency => $"Perfeito. Vamos priorizar a menor latência{sem}, sem aplicar alterações desnecessárias ao seu PC.",
            Objective.Quality => "Perfeito. Vamos manter a qualidade de imagem e corrigir só o que atrapalha o jogo.",
            _ => "Vamos usar um perfil equilibrado e seguro para o seu PC. Dá para mudar quando quiser em Configurações.",
        };
    }
}
