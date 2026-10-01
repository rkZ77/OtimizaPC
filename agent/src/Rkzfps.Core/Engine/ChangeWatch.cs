using Rkzfps.Core.Diagnostics;
using Rkzfps.Core.Model;

namespace Rkzfps.Core.Engine;

/// <summary>O que o vigia guarda de uma análise para comparar com a próxima.</summary>
public sealed record WatchState
{
    public int WindowsBuild { get; init; }
    public string GpuDriver { get; init; } = "";

    /// <summary>Problemas e pontos de atenção da análise anterior (diagnóstico + título).</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];
}

/// <summary>O que mudou entre duas análises, pronto para virar um aviso curto.</summary>
public sealed record WatchReport(
    IReadOnlyList<string> Reverted,
    IReadOnlyList<string> NewProblems,
    string? WindowsChange,
    string? DriverChange)
{
    public bool HasNews => Reverted.Count > 0 || NewProblems.Count > 0 || WindowsChange is not null || DriverChange is not null;

    private string? Cause => WindowsChange is not null ? "Uma atualização do Windows" : DriverChange is not null ? "O driver novo de vídeo" : null;

    public string Title => Reverted.Count > 0
        ? $"{Cause ?? "Algo no PC"} desfez {Plural(Reverted.Count, "correção", "correções")}"
        : NewProblems.Count > 0
            ? $"O RKZFPS achou {Plural(NewProblems.Count, "coisa nova", "coisas novas")} no PC"
            : WindowsChange ?? DriverChange ?? "";

    public string Text
    {
        get
        {
            if (Reverted.Count > 0)
                return $"Voltou ao que era: {List(Reverted)}. Clique para abrir e corrigir de novo, com backup.";
            if (NewProblems.Count > 0)
                return $"{(Cause is null ? "" : Cause + " mudou o PC. ")}Encontrado: {List(NewProblems)}. Clique para ver.";
            return "O RKZFPS conferiu depois da atualização: as correções continuam no lugar e nada novo apareceu.";
        }
    }

    private static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

    // Aviso da bandeja é curto: três nomes e a contagem do resto.
    private static string List(IReadOnlyList<string> items) =>
        string.Join(", ", items.Take(3)) + (items.Count > 3 ? $" e mais {items.Count - 3}" : "");
}

/// <summary>
/// Vigia do PC: compara a análise de agora com a anterior e com o que o
/// RKZFPS aplicou. Atualização do Windows, driver e jogo mudam configuração
/// sem avisar; é isso que faz a correção "sumir" semanas depois. Só lê e
/// avisa: corrigir de novo continua passando pela confirmação de sempre.
/// </summary>
public static class ChangeWatch
{
    public static WatchState StateOf(ScanResult scan) => new()
    {
        WindowsBuild = scan.Snapshot.Os.Build,
        GpuDriver = MainGpu(scan.Snapshot),
        Problems = ProblemKeys(scan).ToList(),
    };

    public static WatchReport Compare(WatchState? before, ScanResult now, IEnumerable<SessionRecord> sessions, Func<string, string?> name)
    {
        // Aplicada, ainda ativa e com desfazer: se a análise pede de novo, algo desfez.
        // Sem desfazer (fechar um programa) não entra: abrir o programa de novo não é "desfazer".
        var active = sessions.SelectMany(s => s.Changes)
            .Where(c => c.Status == ChangeStatus.Applied && c.Applied.Reversible && c.OptimizationId.Length > 0)
            .Select(c => c.OptimizationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reverted = now.Optimizations
            .Where(o => active.Contains(o.Definition.Id) && o.Evaluation.Decision == Decision.Recommended)
            .Select(o => name(o.Definition.Id) ?? o.Definition.Name)
            .Distinct()
            .ToList();

        // Primeira análise vigiada: vira a base, sem aviso de "novo".
        if (before is null)
            return new WatchReport(reverted, [], null, null);

        var old = before.Problems.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fresh = now.Findings
            .Where(f => f.Status is HealthStatus.Problem or HealthStatus.Attention && !old.Contains(Key(f)))
            .Select(f => f.Title)
            .Distinct()
            .ToList();

        var build = now.Snapshot.Os.Build;
        var windows = before.WindowsBuild > 0 && build > 0 && build != before.WindowsBuild
            ? $"Windows atualizado (versão {before.WindowsBuild} para {build})"
            : null;
        var driver = MainGpu(now.Snapshot);
        var driverChange = before.GpuDriver.Length > 0 && driver.Length > 0 && !string.Equals(driver, before.GpuDriver, StringComparison.OrdinalIgnoreCase)
            ? $"Driver de vídeo novo ({before.GpuDriver} para {driver})"
            : null;

        return new WatchReport(reverted, fresh, windows, driverChange);
    }

    private static IEnumerable<string> ProblemKeys(ScanResult scan) =>
        scan.Findings.Where(f => f.Status is HealthStatus.Problem or HealthStatus.Attention).Select(Key).Distinct();

    private static string Key(Finding f) => f.DiagnosticId + "|" + f.Title;

    // A placa que roda o jogo: a dedicada quando existe.
    private static string MainGpu(SystemSnapshot s) =>
        (s.Gpus.FirstOrDefault(g => !g.LikelyIntegrated) ?? s.Gpus.FirstOrDefault())?.DriverVersion ?? "";
}
