using Fpsx.Core.Model;

namespace Fpsx.Core.Optimizations;

internal static class PowerContext
{
    public static IReadOnlyDictionary<string, string> Evidence(PowerInfo p) => Ev.Of(
        ("plano_ativo", $"{p.ActiveSchemeName} ({p.ActiveSchemeGuid})"),
        ("bateria", p.HasBattery ? "sim" : "não"),
        ("na_tomada", p.OnAcPower is null ? null : p.OnAcPower.Value ? "sim" : "não"));

    public static PowerScheme? Find(PowerInfo p, string guid) =>
        p.Schemes.FirstOrDefault(s => PowerSchemes.Is(s.Guid, guid));
}

/// <summary>
/// "Economia de energia" na tomada é o único caso de plano de energia com
/// prejuízo claro e universal: ele limita o estado máximo do processador.
/// </summary>
public sealed class LeavePowerSaverOptimization : IOptimization
{
    public string Id => "power-plan-leave-power-saver";

    public Evaluation Evaluate(EvaluationContext context)
    {
        var p = context.Snapshot.Power;
        if (p?.ActiveSchemeGuid is null)
            return Evaluation.Unknown("Não foi possível ler o plano de energia ativo.");

        var evidence = PowerContext.Evidence(p);
        if (!PowerSchemes.Is(p.ActiveSchemeGuid, PowerSchemes.PowerSaver))
            return Evaluation.Optimal("O plano ativo não é \"Economia de energia\".", evidence);
        if (p.HasBattery && p.OnAcPower != true)
            return Evaluation.NotApplicable("Notebook na bateria: economia de energia é escolha legítima nesse cenário.", evidence);

        var balanced = PowerContext.Find(p, PowerSchemes.Balanced);
        if (balanced is null)
            return Evaluation.NotApplicable("O plano \"Equilibrado\" não existe neste sistema.", evidence);

        return new Evaluation
        {
            Decision = Decision.Recommended,
            Potential = Potential.High,
            Reason = "O PC está na tomada com o plano \"Economia de energia\", que limita a frequência máxima do processador.",
            Evidence = evidence,
            Proposals =
            [
                new Proposal(Id, $"Trocar para o plano \"{balanced.Name}\"",
                    [new PowerSchemeChange(balanced.Guid, balanced.Name)],
                    Potential.High, "Libera o processador para atingir a frequência máxima quando o jogo pede."),
            ],
        };
    }
}

/// <summary>
/// Alto desempenho no desktop: ganho pequeno e condicional (o Equilibrado
/// moderno já sobe o clock rápido). Nunca em notebook na bateria.
/// </summary>
public sealed class HighPerformancePowerOptimization : IOptimization
{
    public string Id => "power-plan-high-performance";

    public Evaluation Evaluate(EvaluationContext context)
    {
        var p = context.Snapshot.Power;
        if (p?.ActiveSchemeGuid is null)
            return Evaluation.Unknown("Não foi possível ler o plano de energia ativo.");

        var evidence = PowerContext.Evidence(p);
        if (PowerSchemes.Is(p.ActiveSchemeGuid, PowerSchemes.HighPerformance) || PowerSchemes.Is(p.ActiveSchemeGuid, PowerSchemes.UltimatePerformance))
            return Evaluation.Optimal("Já está em um plano de alto desempenho.", evidence);
        if (!PowerSchemes.Is(p.ActiveSchemeGuid, PowerSchemes.Balanced))
            return Evaluation.NotApplicable("O plano ativo é personalizado ou de economia. O RKZFPS não substitui plano personalizado.", evidence);
        if (p.HasBattery && p.OnAcPower != true)
            return Evaluation.NotApplicable("Notebook na bateria: alto desempenho só reduziria a autonomia.", evidence);

        var high = PowerContext.Find(p, PowerSchemes.HighPerformance);
        if (high is null)
            return Evaluation.NotApplicable("O plano \"Alto desempenho\" não está disponível (comum em PCs com Modern Standby). Use Configurações > Energia > Modo de energia.", evidence);

        return new Evaluation
        {
            Decision = p.HasBattery ? Decision.Optional : Decision.Recommended,
            Potential = Potential.Low,
            Reason = "Desktop no plano Equilibrado. O plano Alto desempenho mantém o processador pronto para subir o clock, o que pode ajudar na consistência de frametime em jogos leves de CPU.",
            Warning = p.HasBattery ? "Notebook: a alteração aumenta consumo e temperatura, mesmo na tomada." : null,
            Evidence = evidence,
            Proposals =
            [
                new Proposal(Id, $"Trocar para o plano \"{high.Name}\"", [new PowerSchemeChange(high.Guid, high.Name)], Potential.Low,
                    "Ganho pequeno, verifique com o benchmark."),
            ],
        };
    }
}
