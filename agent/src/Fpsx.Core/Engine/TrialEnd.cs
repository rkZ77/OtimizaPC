namespace Fpsx.Core.Engine;

/// <summary>
/// Fim do teste grátis sem assinatura: o app desfaz o que aplicou no teste e
/// o PC volta como estava (decisão do dono, 01/10/2026).
///
/// Só entra o que dá para desfazer de verdade (aplicado e com estado
/// anterior salvo). O resto do desfazer segue a regra de sempre do
/// RollbackManager: valor que a pessoa ou outro programa mudou depois do
/// RKZFPS fica como está, sem --force. Quem decide que o teste acabou é a
/// licença assinada do servidor (status expired + ended_trial), nunca o
/// relógio do PC: plano pago vencido não entra aqui.
/// </summary>
public static class TrialEnd
{
    public static bool Undoable(ChangeRecord c) => c.Status == ChangeStatus.Applied && c.Inverse is not null;

    /// <summary>Sessões com alguma alteração ainda aplicada e reversível, da mais nova para a mais antiga.</summary>
    public static IReadOnlyList<SessionRecord> Pending(IEnumerable<SessionRecord> sessions) =>
        sessions.Where(s => s.Changes.Any(Undoable)).OrderByDescending(s => s.StartedAt).ToList();

    /// <summary>Quantas alterações vão ser desfeitas (o número que o aviso mostra).</summary>
    public static int Count(IEnumerable<SessionRecord> sessions) => sessions.Sum(s => s.Changes.Count(Undoable));

    /// <summary>A sessão tem alteração de sistema: o desfazer dela vai pelo processo elevado.</summary>
    public static bool NeedsAdmin(SessionRecord s) => s.Changes.Any(c => Undoable(c) && c.Applied.RequiresAdmin);
}
