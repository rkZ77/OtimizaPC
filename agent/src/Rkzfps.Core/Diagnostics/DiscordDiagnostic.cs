using Rkzfps.Core.Model;
using Rkzfps.Core.Optimizations;

namespace Rkzfps.Core.Diagnostics;

/// <summary>
/// O Discord fica aberto em quase toda partida. O que ele custa depende do
/// hardware: o achado diz o que vale mudar neste PC. O overlay do jogo fica
/// na conta do Discord, não em arquivo, então é orientação, não alteração.
/// </summary>
public sealed class DiscordDiagnostic : IDiagnostic
{
    public string Id => "discord";

    public IEnumerable<Finding> Run(EvaluationContext context)
    {
        var s = context.Snapshot;
        if (s.Discord is not { } discord)
            yield break;

        var weakGpu = DiscordHardware.WeakGpu(s);
        var fixHw = weakGpu && DiscordHardware.CpuHasRoom(s) && discord.HardwareAcceleration;
        var evidence = new Dictionary<string, string>
        {
            ["aberto"] = discord.Running ? "sim" : "não",
            ["aceleracao_hardware"] = discord.HardwareAcceleration ? "ligada" : "desligada",
            ["abre_com_windows"] = discord.OpensWithWindows ? "sim" : "não",
        };

        var overlay = "No Discord, desligue o overlay do jogo (Configurações > Overlay do jogo) se não usa: ele desenha por cima de cada quadro do jogo e é causa conhecida de travadinhas.";
        yield return new Finding
        {
            DiagnosticId = Id,
            Area = Areas.Startup,
            Status = fixHw ? HealthStatus.Attention : HealthStatus.Info,
            ImpactArea = "FPS",
            Title = fixHw ? "Discord disputando a placa de vídeo com o jogo" : "Discord instalado",
            Detail = fixHw
                ? "A placa de vídeo é o lado fraco deste PC, e o Discord com aceleração de hardware usa a mesma GPU do jogo."
                : weakGpu
                    ? "A aceleração de hardware do Discord já está no melhor valor para este PC."
                    : "Com a placa de vídeo deste PC, o Discord aberto não tira FPS de forma perceptível.",
            Recommendation = overlay,
            FixOptimizationId = fixHw ? "discord-hardware-acceleration-off" : null,
            Evidence = evidence,
        };
    }
}
