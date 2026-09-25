namespace Fpsx.Core.Engine;

/// <summary>Restaurar alteração, restaurar sessão, restaurar tudo (seção 29).</summary>
public sealed class RollbackManager(ISystemAccess system, SessionStore store)
{
    private readonly ChangeExecutor _executor = new(system);

    public SessionRecord RollbackChange(string sessionId, string changeId, bool force)
    {
        var session = store.Load(sessionId) ?? throw new ArgumentException($"Sessão não encontrada: {sessionId}");
        if (session.Changes.All(c => c.Id != changeId))
            throw new ArgumentException($"Alteração {changeId} não existe na sessão {sessionId}");
        return Rollback(session, c => c.Id == changeId, force);
    }

    public SessionRecord RollbackSession(SessionRecord session, bool force) => Rollback(session, _ => true, force);

    public SessionRecord RollbackSession(string sessionId, bool force) =>
        RollbackSession(store.Load(sessionId) ?? throw new ArgumentException($"Sessão não encontrada: {sessionId}"), force);

    /// <summary>Da sessão mais nova para a mais antiga, para cada valor voltar ao estado de antes do FPSX.</summary>
    public IReadOnlyList<SessionRecord> RollbackAll(bool force) =>
        store.All().Select(s => RollbackSession(s, force)).ToList();

    private SessionRecord Rollback(SessionRecord session, Func<ChangeRecord, bool> filter, bool force)
    {
        var changes = session.Changes.ToList();

        // Ordem reversa: se duas alterações tocaram o mesmo valor, a primeira
        // a ser desfeita é a última aplicada.
        foreach (var record in session.Changes.Reverse().Where(filter))
        {
            if (record.Status != ChangeStatus.Applied && record.Status != ChangeStatus.Failed)
                continue;

            ChangeRecord updated;
            if (record.Inverse is null)
            {
                if (record.Status == ChangeStatus.Failed)
                    continue;
                updated = record with
                {
                    Status = ChangeStatus.RollbackSkipped,
                    Error = record.Applied.Reversible
                        ? "O estado anterior não pôde ser lido na hora de aplicar."
                        : "Ação sem estado anterior para restaurar (limpeza de cache ou reparo de rede).",
                };
            }
            else if (!force && record.Status == ChangeStatus.Applied && !_executor.StillApplied(record.Applied))
            {
                // Alguém mudou o valor depois do FPSX. Sobrescrever apagaria a
                // escolha do usuário; só com --force.
                updated = record with { Status = ChangeStatus.RollbackSkipped, Error = "O valor foi modificado depois pelo usuário ou outro programa. Use --force para restaurar mesmo assim." };
            }
            else
            {
                try
                {
                    _executor.Apply(record.Inverse);
                    var check = _executor.Verify(record.Inverse);
                    updated = check.Passed
                        ? record with { Status = ChangeStatus.RolledBack, RolledBackAt = DateTimeOffset.Now, Detail = "restaurado; " + check.Detail }
                        : record with { Status = ChangeStatus.RollbackFailed, Error = "Restauração não confirmada: " + check.Detail };
                }
                catch (Exception ex)
                {
                    updated = record with { Status = ChangeStatus.RollbackFailed, Error = ex.Message };
                }
            }

            OptimizationEngine.Replace(changes, updated);
            store.Log(new ChangeLogEntry(DateTimeOffset.Now, session.Id, record.OptimizationId, "rollback",
                record.Inverse ?? record.Applied, record.Applied, updated.Status == ChangeStatus.RolledBack, updated.Status == ChangeStatus.RolledBack, updated.Detail, updated.Error));
        }

        var allRolled = changes.Where(c => c.Status is not ChangeStatus.Failed and not ChangeStatus.Pending).All(c => c.Status is ChangeStatus.RolledBack or ChangeStatus.RollbackSkipped);
        var result = session with { Changes = changes, Status = allRolled ? SessionStatus.RolledBack : session.Status };
        store.Save(result);
        return result;
    }
}
