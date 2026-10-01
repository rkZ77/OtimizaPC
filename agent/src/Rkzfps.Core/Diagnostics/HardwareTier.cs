using Rkzfps.Core.Model;
using Rkzfps.Core.Optimizations;

namespace Rkzfps.Core.Diagnostics;

public enum HardwareTier
{
    Unknown,
    Low,
    Mid,
    High,
}

public sealed record TierAssessment(HardwareTier Tier, IReadOnlyList<string> Reasons)
{
    public string Label => Tier switch
    {
        HardwareTier.Low => "PC de entrada",
        HardwareTier.Mid => "PC intermediário",
        HardwareTier.High => "PC forte",
        _ => "Nível não identificado",
    };
}

/// <summary>
/// Classifica o PC em entrada, intermediário ou forte, pelo componente mais
/// fraco. Serve para UMA decisão: vale oferecer trocar qualidade gráfica por
/// desempenho? Em PC forte a resposta é não, e o RKZFPS não oferece.
///
/// Os limites são conservadores de propósito. Classificar como fraco um PC
/// que não é faria o app oferecer gráfico pior a quem não precisa; o erro
/// contrário só deixa de oferecer uma opção que o jogador ainda tem no menu.
/// </summary>
public static class HardwareTierClassifier
{
    private const long Gb = 1024L * 1024 * 1024;

    public static TierAssessment Assess(SystemSnapshot s)
    {
        var ramGb = s.Memory is { TotalBytes: > 0 } m ? m.TotalBytes / (double)Gb : (double?)null;
        var dedicated = s.Gpus.Where(g => !g.LikelyIntegrated).ToList();
        var bestVram = dedicated.Select(g => g.VramBytes ?? 0).DefaultIfEmpty(0).Max();
        var threads = s.Cpu?.Threads ?? 0;

        if (ramGb is null && s.Gpus.Count == 0 && threads == 0)
            return new TierAssessment(HardwareTier.Unknown, ["Hardware não lido."]);

        var low = new List<string>();
        // Memória reservada pela placa integrada faz 8 GB virarem ~7,8 no Windows.
        if (ramGb is < 7.5)
            low.Add($"{ramGb:0.#} GB de RAM: jogos atuais pedem 8 GB ou mais.");
        if (s.Gpus.Count > 0 && dedicated.Count == 0)
            low.Add("Só vídeo integrado: a GPU divide memória e energia com o processador.");
        else if (dedicated.Count > 0 && bestVram is > 0 and < 3 * Gb)
            low.Add($"Placa de vídeo com {bestVram / (double)Gb:0.#} GB de memória.");
        if (threads is > 0 and <= 4)
            low.Add($"Processador com {threads} threads.");
        if (low.Count > 0)
            return new TierAssessment(HardwareTier.Low, low);

        var specs = $"{ramGb:0} GB de RAM, placa com {(bestVram > 0 ? $"{bestVram / (double)Gb:0.#} GB" : "memória não lida")} e processador com {threads} threads.";
        // Forte exige os três com folga. Sem VRAM lida, não dá para afirmar.
        if (ramGb >= 15.5 && bestVram >= 8 * Gb && threads >= 12)
            return new TierAssessment(HardwareTier.High, [specs]);

        return new TierAssessment(HardwareTier.Mid, [specs, "Roda os jogos atuais; nos mais pesados, o ajuste fino é pelo menu de vídeo do jogo."]);
    }
}

/// <summary>Mostra o nível do PC no relatório, com o motivo. Só informa.</summary>
public sealed class HardwareTierDiagnostic : IDiagnostic
{
    public string Id => "hardware-tier";

    public IEnumerable<Finding> Run(EvaluationContext context)
    {
        var a = HardwareTierClassifier.Assess(context.Snapshot);
        if (a.Tier == HardwareTier.Unknown)
            yield break;
        yield return new Finding
        {
            DiagnosticId = Id, Area = Areas.Gpu, Status = HealthStatus.Info, ImpactArea = "FPS",
            Title = $"Nível do hardware: {a.Label}",
            Detail = string.Join(" ", a.Reasons),
            Recommendation = a.Tier == HardwareTier.Low
                ? "O RKZFPS oferece configurações leves nos jogos com perfil. Elas trocam qualidade de imagem por desempenho e sempre pedem confirmação."
                : null,
            Evidence = new Dictionary<string, string> { ["nivel"] = a.Tier.ToString().ToUpperInvariant() },
        };
    }
}
