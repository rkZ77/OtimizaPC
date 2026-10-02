using Rkzfps.Core.Diagnostics;
using Rkzfps.Core.Engine;
using Rkzfps.Core.Hardware;
using Rkzfps.Core.Model;

namespace Rkzfps.Core.Games;

// Dicas do menu de vídeo por regra, sem IA: o que vale para este PC e este
// perfil sai do perfil JSON do jogo. A IA fica para "mais dicas", a pedido.
// Dica só recomenda: quem muda o menu é a pessoa (ou o preset, com confirmação).

/// <summary>Quando a dica vale. Lista vazia ou campo null = não restringe.</summary>
public sealed record TipCondition
{
    public IReadOnlyList<HardwareTier> Tiers { get; init; } = [];

    /// <summary>Quanto de imagem a pessoa aceita trocar (NONE, LIGHT, MEDIUM).</summary>
    public IReadOnlyList<GraphicsTradeoff> Tradeoff { get; init; } = [];

    /// <summary>Recurso da placa de vídeo, do hardware.json (reflex, dlss...).</summary>
    public string? GpuFeature { get; init; }

    /// <summary>Só quando a placa NÃO tem este recurso (ex.: FSR para quem não tem DLSS).</summary>
    public string? GpuLacks { get; init; }

    public int? MinHz { get; init; }
    public int? MinThreads { get; init; }
    public int? MaxThreads { get; init; }
    public int? MinRamGb { get; init; }
    public int? MaxRamGb { get; init; }
}

public sealed record GameTip
{
    public string Id { get; init; } = "";

    /// <summary>Nome da opção como aparece no menu do jogo.</summary>
    public string Setting { get; init; } = "";

    /// <summary>Valor sugerido. Aceita {hz} e {hz_cap} (alguns quadros abaixo da taxa do monitor).</summary>
    public string Value { get; init; } = "";

    public string Why { get; init; } = "";

    /// <summary>Menor primeiro.</summary>
    public int Priority { get; init; } = 5;

    /// <summary>latency, fps, stability, image: sobe na lista quando é a prioridade do perfil.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    public TipCondition When { get; init; } = new();
}

public sealed record TipResult(string Id, string Setting, string Value, string Why);

public static class GameTips
{
    public const int MaxTips = 6;

    public static IReadOnlyList<TipResult> For(GameProfile game, SystemSnapshot snapshot, OptimizationProfile tuning, KnownHardware known)
    {
        if (game.Tips.Count == 0)
            return [];
        var tier = HardwareTierClassifier.Assess(snapshot).Tier;
        var hz = Onboarding.MaxRefreshHz(snapshot);
        var threads = snapshot.Cpu?.Threads ?? 0;
        var ramGb = snapshot.Memory is { TotalBytes: > 0 } m ? (int)Math.Round(m.TotalBytes / 1024.0 / 1024 / 1024) : 0;
        // Usuário antigo (sem perguntas): a troca de imagem segue o nível do PC,
        // como o preset sempre fez.
        var tradeoff = tuning.Legacy
            ? tier switch { HardwareTier.Low => GraphicsTradeoff.Medium, HardwareTier.Mid => GraphicsTradeoff.Light, _ => GraphicsTradeoff.None }
            : tuning.GraphicsTradeoff;

        bool Fits(TipCondition w) =>
            (w.Tiers.Count == 0 || w.Tiers.Contains(tier))
            && (w.Tradeoff.Count == 0 || w.Tradeoff.Contains(tradeoff))
            && (w.GpuFeature is null || known.GpuHas(w.GpuFeature))
            && (w.GpuLacks is null || !known.GpuHas(w.GpuLacks))
            && (w.MinHz is null || hz >= w.MinHz)
            && (w.MinThreads is null || threads >= w.MinThreads)
            // Máximo só vale com o valor lido: sem leitura, não chuta "PC fraco".
            && (w.MaxThreads is null || (threads > 0 && threads <= w.MaxThreads))
            && (w.MinRamGb is null || ramGb >= w.MinRamGb)
            && (w.MaxRamGb is null || (ramGb > 0 && ramGb <= w.MaxRamGb));

        // O que o perfil prioriza sobe na lista; o resto segue a prioridade do JSON.
        var focus = new List<string>();
        if (tuning.LatencyPriority == Priority.High)
            focus.Add("latency");
        if (tuning.StabilityPriority == Priority.High)
            focus.Add("stability");
        if (tradeoff != GraphicsTradeoff.None)
            focus.Add("fps");

        var cap = hz > 0 ? Math.Max(30, hz - 3) : 0;
        return game.Tips
            .Where(t => Fits(t.When))
            .OrderBy(t => t.Tags.Any(focus.Contains) ? 0 : 1)
            .ThenBy(t => t.Priority)
            .Take(MaxTips)
            .Select(t => new TipResult(t.Id, t.Setting, Fill(t.Value, hz, cap), Fill(t.Why, hz, cap)))
            .ToList();
    }

    private static string Fill(string text, int hz, int cap) => hz <= 0
        ? text.Replace("{hz}", "a taxa do monitor").Replace("{hz_cap}", "um pouco abaixo da taxa do monitor")
        : text.Replace("{hz_cap}", cap.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Replace("{hz}", hz.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
