using Rkzfps.Core.Games;
using Rkzfps.Core.Model;

namespace Rkzfps.Core.Optimizations;

public sealed record EvaluationContext(SystemSnapshot Snapshot, IReadOnlyList<GameProfile> GameProfiles)
{
    public GameInstall? Game(string id) => Snapshot.Games.FirstOrDefault(g => g.GameId == id);
}

/// <summary>
/// Uma ação concreta que o usuário pode aceitar. Uma otimização pode gerar
/// várias (uma por item de inicialização, uma por monitor).
/// </summary>
public sealed record Proposal(string Id, string Title, IReadOnlyList<Change> Changes, Potential Potential, string Rationale);

public sealed record Evaluation
{
    public Decision Decision { get; init; }

    /// <summary>Por que chegamos nesta decisão, em pt-BR, mostrado ao usuário.</summary>
    public string Reason { get; init; } = "";

    public Potential Potential { get; init; }
    public IReadOnlyList<Proposal> Proposals { get; init; } = [];

    /// <summary>Estado lido que embasou a decisão (vai para o relatório e o log).</summary>
    public IReadOnlyDictionary<string, string> Evidence { get; init; } = new Dictionary<string, string>();

    /// <summary>Aviso extra antes de aplicar (notebook, perda de função etc.).</summary>
    public string? Warning { get; init; }

    public static Evaluation NotApplicable(string reason, IReadOnlyDictionary<string, string>? evidence = null) =>
        new() { Decision = Decision.NotApplicable, Reason = reason, Evidence = evidence ?? Empty };

    public static Evaluation Optimal(string reason, IReadOnlyDictionary<string, string>? evidence = null) =>
        new() { Decision = Decision.AlreadyOptimal, Reason = reason, Evidence = evidence ?? Empty };

    public static Evaluation Unknown(string reason) =>
        new() { Decision = Decision.Unknown, Reason = reason };

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();
}

/// <summary>
/// Handler compilado de uma otimização. O catálogo JSON só descreve; a lógica
/// de detecção e as alterações vivem aqui, dentro do binário assinado.
/// </summary>
public interface IOptimization
{
    string Id { get; }

    Evaluation Evaluate(EvaluationContext context);
}
