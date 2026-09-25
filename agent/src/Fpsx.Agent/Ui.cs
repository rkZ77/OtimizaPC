using Fpsx.Core.Model;

namespace Fpsx.Agent;

/// <summary>Saída de terminal. Sem emoji e sem cor como única fonte de informação: todo status tem rótulo em texto.</summary>
public static class Ui
{
    public static bool Interactive => !Console.IsInputRedirected;

    public static void Title(string text)
    {
        Console.WriteLine();
        Write(text.ToUpperInvariant(), ConsoleColor.Cyan);
        Console.WriteLine();
        Console.WriteLine(new string('-', Math.Min(72, Math.Max(text.Length, 24))));
    }

    public static void Line(string text = "") => Console.WriteLine(text);

    public static void Muted(string text) => WriteLine(text, ConsoleColor.DarkGray);

    public static void Error(string text) => WriteLine(text, ConsoleColor.Red);

    public static void Warn(string text) => WriteLine(text, ConsoleColor.Yellow);

    public static void Ok(string text) => WriteLine(text, ConsoleColor.Green);

    public static void WriteLine(string text, ConsoleColor color)
    {
        Write(text, color);
        Console.WriteLine();
    }

    public static void Write(string text, ConsoleColor color)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = old;
    }

    public static void Status(HealthStatus status)
    {
        var (label, color) = status switch
        {
            HealthStatus.Ok => ("OK      ", ConsoleColor.Green),
            HealthStatus.Info => ("INFO    ", ConsoleColor.Gray),
            HealthStatus.Attention => ("ATENÇÃO ", ConsoleColor.Yellow),
            HealthStatus.Problem => ("PROBLEMA", ConsoleColor.Red),
            _ => ("?       ", ConsoleColor.DarkGray),
        };
        Write(label, color);
    }

    public static string DecisionLabel(Decision d) => d switch
    {
        Decision.Recommended => "RECOMENDADA",
        Decision.Optional => "OPCIONAL",
        Decision.AlreadyOptimal => "JÁ OTIMIZADO",
        Decision.NotApplicable => "NÃO SE APLICA",
        Decision.Blocked => "BLOQUEADA",
        _ => "DESCONHECIDO",
    };

    public static ConsoleColor DecisionColor(Decision d) => d switch
    {
        Decision.Recommended => ConsoleColor.Yellow,
        Decision.Optional => ConsoleColor.Gray,
        Decision.AlreadyOptimal => ConsoleColor.Green,
        Decision.Blocked => ConsoleColor.DarkYellow,
        _ => ConsoleColor.DarkGray,
    };

    public static string PotentialLabel(Potential p) => p switch
    {
        Potential.High => "alto",
        Potential.Moderate => "moderado",
        Potential.Low => "baixo",
        _ => "nenhum (troubleshooting)",
    };

    public static string ClassificationLabel(Classification c) => c switch
    {
        Classification.Proven => "PROVEN",
        Classification.Conditional => "CONDITIONAL",
        Classification.Troubleshooting => "TROUBLESHOOTING",
        Classification.Experimental => "EXPERIMENTAL",
        _ => "NOT_RECOMMENDED",
    };

    /// <summary>Pergunta sim/não. Sem terminal interativo, a resposta é sempre "não".</summary>
    public static bool Confirm(string question)
    {
        if (!Interactive)
            return false;
        Write(question + " [s/N] ", ConsoleColor.White);
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        return answer is "s" or "sim" or "y" or "yes";
    }

    public static char Choose(string question, string options, char fallback)
    {
        if (!Interactive)
            return fallback;
        Write(question + " ", ConsoleColor.White);
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(answer) || !options.Contains(answer[0]) ? fallback : answer[0];
    }
}
