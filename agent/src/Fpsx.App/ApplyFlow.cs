using System.Text;
using Fpsx.Core.Engine;
using Fpsx.Core.Model;

namespace Fpsx.App;

/// <summary>
/// O caminho único de "aplicar" em todas as telas: confere bloqueios, mostra
/// exatamente o que muda, pede confirmação, aplica com backup e mostra o
/// resultado. Responde às perguntas da seção 45: o que muda, qual o risco,
/// posso desfazer, funcionou?
/// </summary>
public static class ApplyFlow
{
    public static async Task<SessionRecord?> RunAsync(IReadOnlyList<string> ids, IProgress<string> progress)
    {
        var host = AppHost.Current;
        var scan = host.Scan;
        if (scan is null || ids.Count == 0)
            return null;

        var items = OptimizationEngine.Expand(scan, ids)
            .Select(id => scan.FindProposal(id))
            .Where(found => found.HasValue)
            .Select(found => found!.Value)
            .ToList();
        if (items.Count == 0)
        {
            Dialogs.Info("Nada para aplicar", "Os itens escolhidos não estão mais disponíveis. Rode a análise de novo.");
            return null;
        }

        if (!ResolveBlocked(items.Select(i => i.Result).Distinct().ToList()))
            return null;

        if (host.Ctx.TrialLimit() is { } trial && !FitTrial(trial, ref items))
            return null;

        var experimental = items.Any(i => i.Result.Definition.Classification == Classification.Experimental);
        if (experimental && !Dialogs.Confirm("Otimização experimental",
                "Você escolheu uma otimização EXPERIMENTAL. O resultado varia por PC e jogo e pode piorar o desempenho.\n\n" +
                "Meça com o RKZFPS Benchmark antes e depois, e desfaça pelo Histórico se não houver ganho.", "Entendo, continuar"))
            return null;

        var elevated = items.Where(i => i.Result.RequiresElevation).ToList();
        var confirm = Describe(items) + (elevated.Count > 0
            ? "\nO Windows vai pedir sua permissão para as alterações de sistema. Isso é normal: o RKZFPS usa a permissão só para elas e fecha em seguida."
            : "");
        if (!Dialogs.Confirm("Confirmar alterações", confirm, "Aplicar com backup"))
            return null;

        var normal = items.Where(i => !i.Result.RequiresElevation).Select(i => i.Proposal.Id).ToList();
        var sessions = new List<SessionRecord>();
        if (normal.Count > 0)
            sessions.Add(await host.ApplyAsync(normal, experimental, progress));
        if (elevated.Count > 0)
        {
            var admin = await host.ApplyElevatedAsync(elevated.Select(i => i.Proposal.Id).ToList(), experimental, progress);
            if (admin is null)
                Dialogs.Info("Permissão recusada", "Sem a permissão do Windows, as alterações de sistema não foram aplicadas. Nada foi mudado nelas.");
            else
                sessions.Add(admin);
        }

        if (sessions.Count > 0)
            Dialogs.Info("Resultado", string.Join("\n\n", sessions.Select(Summary)));
        return sessions.LastOrDefault();
    }

    /// <summary>
    /// Teste grátis: avisa antes quando a escolha passa do limite e deixa na
    /// lista só o que cabe, para a confirmação mostrar exatamente o que muda.
    /// O motor aplica o mesmo limite de novo (o processo elevado também).
    /// </summary>
    private static bool FitTrial(TrialQuota trial, ref List<(OptimizationResult Result, Fpsx.Core.Optimizations.Proposal Proposal)> items)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fits = items.Where(i =>
        {
            var id = i.Result.Definition.Id;
            if (!trial.Allows(id, taken))
                return false;
            taken.Add(id);
            return true;
        }).ToList();
        if (fits.Count == items.Count)
            return true;

        var left = items.Select(i => i.Result.Definition.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count(id => !trial.Used.Contains(id));
        var max = TrialQuota.MaxOptimizations;
        if (fits.Count == 0)
        {
            Upsell.Offer("pro", left, $"Seu teste grátis já aplicou as {max} correções que ele libera. As que ele fez continuam no PC e podem ser desfeitas no Histórico.");
            return false;
        }

        var choice = Dialogs.Show("Teste grátis",
            $"No teste grátis o RKZFPS aplica até {max} correções diferentes, e ainda cabe {trial.Remaining}. Das escolhidas, ele aplica agora as que cabem, na ordem da lista.\n\nAssine para aplicar todas.",
            $"Aplicar {trial.Remaining}", Upsell.ButtonLabel("pro"), "Cancelar");
        if (choice == 1)
            Upsell.Go("pro", left);
        if (choice != 0)
            return false;
        items = fits;
        return true;
    }

    /// <summary>Só plano bloqueia: permissão de administrador é pedida na hora de aplicar.</summary>
    private static bool ResolveBlocked(IReadOnlyList<OptimizationResult> results)
    {
        var blocked = results.Where(r => r.Decision == Decision.Blocked).ToList();
        if (blocked.Count == 0)
            return true;

        var first = blocked.First();
        var scan = AppHost.Current.Scan!;
        if (Upsell.RequiredPlan(scan, blocked) is not { } plan)
        {
            // Travada por outro motivo (desativada pelo administrador): plano não resolve.
            Dialogs.Info("Indisponível", $"\"{first.Definition.Name}\": {first.Reason}");
            return false;
        }

        // O número que vai para o pagamento é o que espera no PC inteiro, não só neste clique.
        var waiting = scan.Optimizations.Count(o => Upsell.IsPlanLocked(scan.Plan, o) && o.Evaluation.Decision == Decision.Recommended);
        Upsell.Offer(plan, Math.Max(waiting, blocked.Count), $"\"{first.Definition.Name}\": {first.Reason}");
        return false;
    }

    private static string Describe(IEnumerable<(OptimizationResult Result, Fpsx.Core.Optimizations.Proposal Proposal)> items)
    {
        var sb = new StringBuilder("O RKZFPS vai criar um backup antes de cada alteração e conferir o resultado depois.\n");
        foreach (var group in items.GroupBy(i => i.Result))
        {
            var def = group.Key.Definition;
            sb.Append($"\n{def.Name} (risco {def.Risk.ToString().ToLowerInvariant()})\n");
            foreach (var (_, p) in group)
                foreach (var c in p.Changes)
                    sb.Append($"  • {c.Describe()}{(c.Reversible ? "" : " (sem desfazer)")}{(c.RequiresReboot ? " (exige reinício)" : "")}\n");
            if (group.Key.Evaluation.Warning is { } w)
                sb.Append($"  Atenção: {w}\n");
        }

        return sb.ToString();
    }

    private static string Summary(SessionRecord s)
    {
        var applied = s.Changes.Count(c => c.Status == ChangeStatus.Applied);
        var failed = s.Changes.Where(c => c.Status is ChangeStatus.Failed or ChangeStatus.RollbackFailed).ToList();
        var sb = new StringBuilder();
        sb.Append(s.Status switch
        {
            SessionStatus.Completed => $"{applied} alteração(ões) aplicada(s) e verificada(s).",
            SessionStatus.RolledBack => "A sessão foi restaurada: tudo voltou ao estado anterior.",
            SessionStatus.Cancelled => $"Parado após uma falha. {applied} alteração(ões) aplicada(s) antes dela.",
            _ => $"{applied} alteração(ões) aplicada(s), com falhas.",
        });
        foreach (var f in failed)
            sb.Append($"\n\nFalhou: {f.Applied.Describe()}\n{f.Error}");
        foreach (var skip in s.Skipped)
            sb.Append($"\n\nIgnorada: {skip.Reason}");
        if (s.Changes.Any(c => c.Status == ChangeStatus.Applied && c.Applied.RequiresReboot))
            sb.Append("\n\nReinicie o PC para concluir as alterações que exigem reinício.");
        if (applied > 0)
            sb.Append("\n\nTudo pode ser desfeito na tela Histórico. Para medir o efeito no jogo, use o RKZFPS Benchmark.");
        return sb.ToString();
    }
}
