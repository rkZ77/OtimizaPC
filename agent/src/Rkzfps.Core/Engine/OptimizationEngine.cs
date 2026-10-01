using Rkzfps.Core.Model;

namespace Rkzfps.Core.Engine;

public enum FailureChoice
{
    /// <summary>Desfaz tudo que esta sessão já aplicou.</summary>
    Restore,

    /// <summary>Ignora a falha e segue para a próxima otimização.</summary>
    Continue,

    /// <summary>Para aqui e mantém o que já foi aplicado.</summary>
    Cancel,
}

public sealed record FailureContext(string ProposalId, string Description, string Error);

public sealed record ApplyOptions
{
    public bool AllowExperimental { get; init; }
    public bool DryRun { get; init; }

    /// <summary>
    /// Chamado quando uma alteração falha. Sem callback, o padrão é Cancel:
    /// o RKZFPS não continua silenciosamente depois de um erro (seção 43).
    /// </summary>
    public Func<FailureContext, FailureChoice>? OnFailure { get; init; }

    /// <summary>Teste grátis: quantas otimizações diferentes ainda cabem. null = sem limite (plano pago).</summary>
    public TrialQuota? Trial { get; init; }

    /// <summary>Marca a sessão como temporária do modo Gaming (nome do jogo) desde o primeiro save.</summary>
    public string? GamingGame { get; init; }
}

/// <summary>Criar backup, aplicar, validar e registrar: o resto do fluxo da seção 25.</summary>
public sealed class OptimizationEngine(ISystemAccess system, SessionStore store)
{
    private readonly ChangeExecutor _executor = new(system);

    public SessionRecord Apply(ScanResult scan, IReadOnlyList<string> proposalIds, ApplyOptions options)
    {
        var session = new SessionRecord
        {
            Id = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6],
            StartedAt = DateTimeOffset.Now,
            ProfileId = scan.ProfileId,
            CatalogVersion = scan.CatalogVersion,
            MachineName = Environment.MachineName,
            Status = SessionStatus.Running,
            // Marcada desde o início: se o app cair no meio, a próxima abertura
            // sabe que é temporária e restaura.
            GamingGame = options.GamingGame,
            AlreadyOptimal = scan.Optimizations.Where(o => o.Decision == Decision.AlreadyOptimal).Select(o => o.Definition.Id).ToList(),
        };
        var changes = new List<ChangeRecord>();
        var skipped = new List<SkippedItem>();

        // Valida a lista inteira antes de tocar em qualquer coisa: violação de
        // segurança aborta a sessão sem aplicar nem a primeira alteração.
        var work = new List<(OptimizationResult Result, Optimizations.Proposal Proposal)>();
        var takenNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in Expand(scan, proposalIds).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (scan.FindProposal(id) is not { } found)
            {
                skipped.Add(new SkippedItem(id, "Não existe no scan atual."));
                continue;
            }

            var (result, proposal) = found;
            if (result.Decision is not (Decision.Recommended or Decision.Optional))
            {
                skipped.Add(new SkippedItem(id, result.Decision == Decision.AlreadyOptimal ? "Já estava otimizado." : result.Reason));
                continue;
            }

            // Sem administrador a escrita falharia no meio da sessão: melhor
            // pular com o motivo. O app roda estas pelo processo elevado.
            if (result.RequiresElevation)
            {
                skipped.Add(new SkippedItem(id, "Precisa da permissão de administrador do Windows."));
                continue;
            }

            if (result.Definition.Classification == Classification.Experimental && !options.AllowExperimental)
            {
                skipped.Add(new SkippedItem(id, "Experimental: exige autorização explícita."));
                continue;
            }

            // Limite do teste no motor, e não só na tela: o processo elevado e o
            // CLI passam por aqui também.
            if (options.Trial is { } trial)
            {
                if (!trial.Allows(result.Definition.Id, takenNow))
                {
                    skipped.Add(new SkippedItem(id, TrialQuota.SkipReason));
                    continue;
                }
                takenNow.Add(result.Definition.Id);
            }

            foreach (var change in proposal.Changes)
                SafetyPolicy.Validate(change);
            work.Add(found);
        }

        if (options.DryRun)
            return session with { Status = SessionStatus.Completed, FinishedAt = DateTimeOffset.Now, Skipped = skipped };

        store.Save(session with { Skipped = skipped });
        var status = SessionStatus.Completed;

        foreach (var (result, proposal) in work)
        {
            var failure = ApplyProposal(session.Id, result.Definition.Id, proposal, changes, () => store.Save(session with { Changes = changes, Skipped = skipped }));
            if (failure is null)
                continue;

            var choice = options.OnFailure?.Invoke(new FailureContext(proposal.Id, proposal.Title, failure)) ?? FailureChoice.Cancel;
            if (choice == FailureChoice.Continue)
            {
                status = SessionStatus.Failed;
                continue;
            }

            if (choice == FailureChoice.Restore)
            {
                var partial = session with { Changes = changes, Skipped = skipped };
                var rolled = new RollbackManager(system, store).RollbackSession(partial, force: false);
                rolled = rolled with { Status = SessionStatus.RolledBack, FinishedAt = DateTimeOffset.Now };
                store.Save(rolled);
                return rolled;
            }

            status = SessionStatus.Cancelled;
            skipped.AddRange(work.SkipWhile(w => w.Proposal != proposal).Skip(1).Select(w => new SkippedItem(w.Proposal.Id, "Cancelado após falha anterior.")));
            break;
        }

        var final = session with { Changes = changes, Skipped = skipped, Status = status, FinishedAt = DateTimeOffset.Now };
        store.Save(final);
        return final;
    }

    /// <summary>Retorna a mensagem de erro, ou null quando tudo passou.</summary>
    private string? ApplyProposal(string sessionId, string optimizationId, Optimizations.Proposal proposal, List<ChangeRecord> changes, Action persist)
    {
        foreach (var change in proposal.Changes)
        {
            var record = new ChangeRecord
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                OptimizationId = optimizationId,
                ProposalId = proposal.Id,
                Applied = change,
                At = DateTimeOffset.Now,
                Status = ChangeStatus.Pending,
            };

            try
            {
                record = record with { Inverse = change.Reversible ? _executor.CaptureInverse(change) : null };
                changes.Add(record);
                persist();

                var detail = _executor.Apply(change);
                var verification = _executor.Verify(change);
                record = record with
                {
                    Status = verification.Passed ? ChangeStatus.Applied : ChangeStatus.Failed,
                    Verified = verification.Passed,
                    Detail = $"{detail}; verificação: {verification.Detail}",
                    Error = verification.Passed ? null : "Verificação falhou: " + verification.Detail,
                };
            }
            catch (Exception ex)
            {
                record = record with { Status = ChangeStatus.Failed, Error = ex.Message };
            }

            Replace(changes, record);
            persist();
            store.Log(new ChangeLogEntry(record.At, sessionId, optimizationId, "apply", change, record.Inverse,
                record.Status == ChangeStatus.Applied, record.Verified, record.Detail, record.Error));

            if (record.Status == ChangeStatus.Failed)
                return record.Error;
        }

        return null;
    }

    /// <summary>
    /// Id de otimização (sem ":") vale por todas as propostas dela: é o botão
    /// "Resolver tudo" do app. Id de proposta passa direto.
    /// </summary>
    public static IEnumerable<string> Expand(ScanResult scan, IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            var opt = scan.Optimizations.FirstOrDefault(o => o.Definition.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (opt is null || opt.Evaluation.Proposals.Any(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                yield return id;
                continue;
            }

            foreach (var p in opt.Evaluation.Proposals)
                yield return p.Id;
        }
    }

    internal static void Replace(List<ChangeRecord> list, ChangeRecord record)
    {
        var i = list.FindIndex(c => c.Id == record.Id);
        if (i >= 0)
            list[i] = record;
        else
            list.Add(record);
    }
}
