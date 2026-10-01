using Rkzfps.Core.Model;

namespace Rkzfps.Core.Optimizations;

/// <summary>
/// Monitor de 144 Hz rodando a 60 Hz é um dos problemas mais comuns e de
/// maior impacto visível: o jogo pode render 300 FPS e a tela só mostrar 60.
/// </summary>
public sealed class DisplayRefreshOptimization : IOptimization
{
    public string Id => "display-refresh-rate-max";

    public Evaluation Evaluate(EvaluationContext context)
    {
        var displays = context.Snapshot.Displays;
        if (displays.Count == 0)
            return Evaluation.Unknown("Não foi possível ler os modos de vídeo dos monitores.");

        var evidence = displays.ToDictionary(
            d => d.DeviceName,
            d => $"{d.Width}x{d.Height} a {d.CurrentHz} Hz (máximo nesta resolução: {d.MaxHzAtCurrentResolution} Hz)");

        // Tolerância de 1 Hz: 59/60 e 143/144 são o mesmo modo com arredondamento diferente.
        var below = displays.Where(d => d.MaxHzAtCurrentResolution > d.CurrentHz + 1).ToList();
        if (below.Count == 0)
            return Evaluation.Optimal("Todos os monitores já estão na taxa de atualização máxima da resolução atual.", evidence);

        return new Evaluation
        {
            Decision = Decision.Recommended,
            Potential = Potential.High,
            Reason = "Há monitor rodando abaixo da taxa de atualização que ele suporta. A tela descarta quadros que o PC já renderizou.",
            Warning = "Se a tela piscar ou ficar preta, o RKZFPS volta ao modo anterior pelo rollback. Cabos antigos (HDMI 1.4) podem não suportar a taxa máxima.",
            Evidence = evidence,
            Proposals = below.Select(d => new Proposal(
                $"{Id}:{d.DeviceName}",
                $"{(string.IsNullOrEmpty(d.FriendlyName) ? d.DeviceName : d.FriendlyName)}: {d.CurrentHz} Hz para {d.MaxHzAtCurrentResolution} Hz",
                [new DisplayRefreshChange(d.DeviceName, d.Width, d.Height, d.MaxHzAtCurrentResolution)],
                Potential.High,
                "Afeta suavidade e latência percebida, não o FPS renderizado.")).ToList(),
        };
    }
}
