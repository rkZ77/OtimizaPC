using Rkzfps.Core.Model;

namespace Rkzfps.Core.Optimizations;

internal static class Ev
{
    public static IReadOnlyDictionary<string, string> Of(params (string Key, string? Value)[] items) =>
        items.ToDictionary(i => i.Key, i => i.Value ?? "desconhecido");
}

/// <summary>Game Mode só é mexido quando alguém o DESLIGOU: no Windows 11 ele vem ligado.</summary>
public sealed class GameModeOptimization : IOptimization
{
    public string Id => "game-mode-enable";

    public Evaluation Evaluate(EvaluationContext context)
    {
        var s = context.Snapshot;
        var value = s.Gaming.AutoGameModeValue;
        var evidence = Ev.Of(("AutoGameModeEnabled", value?.ToString() ?? "ausente (padrão: ligado)"), ("build", s.Os.Build.ToString()));

        // Game Mode existe desde o Windows 10 1703 (build 15063).
        if (s.Os.Build is > 0 and < 15063)
            return Evaluation.NotApplicable("Versão do Windows sem Game Mode.", evidence);
        if (value is null or 1)
            return Evaluation.Optimal("Game Mode já está ativado.", evidence);

        return new Evaluation
        {
            Decision = Decision.Recommended,
            Potential = Potential.Low,
            Reason = "Game Mode foi desativado. Ele prioriza o jogo em primeiro plano e segura atualizações e notificações durante a partida.",
            Evidence = evidence,
            Proposals =
            [
                new Proposal(Id, "Ativar o Game Mode",
                    [new RegistryValueChange(RegistryRoot.CurrentUser, RegistryPaths.GameBar, "AutoGameModeEnabled", RegValue.DWord(1), Label: "Ativar o Game Mode do Windows")],
                    Potential.Low, "Restaura o padrão do Windows."),
            ],
        };
    }
}

/// <summary>
/// Desliga só a gravação contínua em segundo plano ("Gravar o que aconteceu"),
/// que mantém o encoder de vídeo da GPU trabalhando o tempo todo. A captura
/// manual (Win+Alt+R) continua funcionando.
/// </summary>
public sealed class BackgroundRecordingOptimization : IOptimization
{
    public string Id => "game-capture-background-recording-disable";

    public Evaluation Evaluate(EvaluationContext context)
    {
        var g = context.Snapshot.Gaming;
        var evidence = Ev.Of(
            ("HistoricalCaptureEnabled", g.BackgroundRecordingValue?.ToString() ?? "ausente (padrão: desligado)"),
            ("AppCaptureEnabled", g.AppCaptureValue?.ToString() ?? "ausente"));

        if (g.BackgroundRecordingValue != 1)
            return Evaluation.Optimal("A gravação em segundo plano já está desligada.", evidence);

        return new Evaluation
        {
            Decision = Decision.Recommended,
            Potential = Potential.Low,
            Reason = "A Game Bar está gravando continuamente os últimos minutos de jogo. Isso ocupa o encoder da GPU, disco e memória durante toda a partida.",
            Warning = "Se você usa \"Gravar o que aconteceu\" para salvar jogadas, ao desligar você perde esse recurso. A gravação manual continua disponível.",
            Evidence = evidence,
            Proposals =
            [
                new Proposal(Id, "Desligar a gravação em segundo plano da Game Bar",
                    [new RegistryValueChange(RegistryRoot.CurrentUser, RegistryPaths.GameDvr, "HistoricalCaptureEnabled", RegValue.DWord(0), Label: "Desligar a gravação em segundo plano da Game Bar")],
                    Potential.Low, "Libera o encoder de vídeo e a escrita contínua em disco."),
            ],
        };
    }
}

/// <summary>
/// HAGS tem resultado misto por jogo e driver: fica EXPERIMENTAL, nunca
/// automático. Só é oferecido quando a chave existe com valor 1, o que indica
/// que GPU e driver suportam e alguém (ou o padrão) deixou desligado.
/// </summary>
public sealed class HagsOptimization : IOptimization
{
    public string Id => "hags-enable";

    public Evaluation Evaluate(EvaluationContext context)
    {
        var value = context.Snapshot.Gaming.HagsValue;
        var evidence = Ev.Of(("HwSchMode", value?.ToString() ?? "ausente"));

        if (value is null)
            return Evaluation.NotApplicable("GPU ou driver sem suporte declarado a agendamento acelerado por hardware.", evidence);
        if (value == 2)
            return Evaluation.Optimal("Agendamento de GPU acelerado por hardware já está ativado.", evidence);

        return new Evaluation
        {
            Decision = Decision.Optional,
            Potential = Potential.Low,
            Reason = "HAGS pode reduzir a latência de CPU no envio de quadros em alguns jogos, e é exigido por recursos como Frame Generation. Em outros casos não muda nada ou piora. Só vale com medição antes e depois.",
            Warning = "Experimental. Exige reinício. Meça com o RKZFPS Benchmark antes e depois e desfaça se não houver ganho.",
            Evidence = evidence,
            Proposals =
            [
                new Proposal(Id, "Ativar agendamento de GPU acelerado por hardware",
                    [new RegistryValueChange(RegistryRoot.LocalMachine, RegistryPaths.GraphicsDrivers, "HwSchMode", RegValue.DWord(2), NeedsReboot: true, Label: "Ativar o agendamento de GPU acelerado por hardware")],
                    Potential.Low, "Resultado depende do jogo e do driver."),
            ],
        };
    }
}

/// <summary>
/// "Aumentar precisão do ponteiro" é aceleração: o mesmo movimento da mão
/// anda mais ou menos na tela conforme a velocidade. Vem ligada no Windows.
/// Jogo com entrada bruta (CS2, Valorant, a maioria dos FPS atuais) ignora
/// isso, mas jogo sem entrada bruta, menus e o próprio Windows não. Não mexe
/// em FPS: é mira consistente. Por ser preferência, nunca entra na seleção
/// automática; fica como opcional.
/// </summary>
public sealed class MouseAccelerationOptimization : IOptimization
{
    public string Id => "mouse-acceleration-off";

    public Evaluation Evaluate(EvaluationContext context)
    {
        var speed = context.Snapshot.Gaming.MouseSpeedValue;
        var evidence = Ev.Of(("MouseSpeed", speed ?? "não lido"));

        if (speed is null)
            return Evaluation.NotApplicable("Não deu para ler a configuração do mouse.", evidence);
        if (speed == "0")
            return Evaluation.Optimal("A aceleração do mouse já está desligada.", evidence);

        static RegistryValueChange Set(string name, string value, string label) =>
            new(RegistryRoot.CurrentUser, RegistryPaths.Mouse, name, new RegValue(RegistryKind.String, value), Label: label);

        return new Evaluation
        {
            Decision = Decision.Optional,
            Potential = Potential.Low,
            Reason = "A aceleração do mouse está ligada: o mesmo movimento da mão anda mais ou menos na tela conforme a velocidade. Desligada, a mira fica igual toda vez, o que ajuda a memória muscular.",
            Warning = "Nos primeiros minutos o ponteiro vai parecer diferente. Jogos com entrada bruta (CS2, Valorant) já ignoram essa opção.",
            Evidence = evidence,
            Proposals =
            [
                new Proposal(Id, "Desligar a aceleração do mouse",
                    [
                        Set("MouseSpeed", "0", "Desligar \"Aumentar precisão do ponteiro\""),
                        Set("MouseThreshold1", "0", "Zerar o primeiro limiar de aceleração"),
                        Set("MouseThreshold2", "0", "Zerar o segundo limiar de aceleração"),
                    ],
                    Potential.Low, "Mesma opção de Configurações > Bluetooth e dispositivos > Mouse."),
            ],
        };
    }
}
