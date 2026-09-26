using Fpsx.Core.Diagnostics;
using Fpsx.Core.Model;

namespace Fpsx.Core.Optimizations;

/// <summary>
/// Leitura do hardware para decidir sobre o Discord aberto junto com o jogo.
/// Com aceleração de hardware, o Discord desenha a janela e decodifica vídeo
/// (chamada, transmissão de amigo) na GPU, a mesma do jogo. Desligar muda esse
/// trabalho para o processador. Só compensa quando a GPU é o lado fraco e o
/// processador tem folga; no caso contrário, troca um gargalo por outro.
/// </summary>
public static class DiscordHardware
{
    private const long Gb = 1024L * 1024 * 1024;

    public static bool WeakGpu(SystemSnapshot s)
    {
        if (s.Gpus.Count == 0)
            return false;
        var dedicated = s.Gpus.Where(g => !g.LikelyIntegrated).ToList();
        if (dedicated.Count == 0)
            return true;
        var vram = dedicated.Select(g => g.VramBytes ?? 0).Max();
        // VRAM não lida: não dá para afirmar que a placa é fraca.
        return vram is > 0 and < 4 * Gb;
    }

    /// <summary>Processador que absorve o desenho do Discord sem roubar do jogo.</summary>
    public static bool CpuHasRoom(SystemSnapshot s) => s.Cpu?.Threads >= 6;
}

public sealed class DiscordHardwareAccelerationOptimization : IOptimization
{
    public string Id => "discord-hardware-acceleration-off";

    public Evaluation Evaluate(EvaluationContext context)
    {
        var s = context.Snapshot;
        if (s.Discord is not { } discord)
            return Evaluation.NotApplicable("Discord não instalado nesta conta do Windows.");

        var weakGpu = DiscordHardware.WeakGpu(s);
        var cpuRoom = DiscordHardware.CpuHasRoom(s);
        var evidence = Ev.Of(
            ("enableHardwareAcceleration", discord.HardwareAcceleration ? "true" : "false"),
            ("gpu_fraca", weakGpu ? "sim" : "não"),
            ("threads", s.Cpu?.Threads.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        if (!weakGpu)
            return Evaluation.NotApplicable("Com a placa de vídeo deste PC, o Discord na GPU não pesa no jogo. Desligar a aceleração só passaria trabalho para o processador.", evidence);
        if (!cpuRoom)
            return Evaluation.NotApplicable("Neste PC a placa de vídeo e o processador são os dois limitados: desligar a aceleração do Discord só troca um gargalo por outro.", evidence);
        if (!discord.HardwareAcceleration)
            return Evaluation.Optimal("A aceleração de hardware do Discord já está desligada.", evidence);

        return new Evaluation
        {
            Decision = Decision.Recommended,
            Potential = Potential.Low,
            Reason = "A placa de vídeo é o lado fraco deste PC e o Discord aberto usa a mesma GPU do jogo. O processador tem folga para desenhar o Discord no lugar dela.",
            Warning = "Feche o Discord antes, inclusive o ícone perto do relógio. Chamada de vídeo e transmissão no Discord passam a usar mais o processador.",
            Evidence = evidence,
            Proposals =
            [
                new Proposal(Id, "Desligar a aceleração de hardware do Discord",
                    [new AppSettingChange("discord", "enableHardwareAcceleration", "false")],
                    Potential.Low, "Mesma opção de Discord > Configurações > Avançado > Aceleração de hardware."),
            ],
        };
    }
}
