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

        var experimental = items.Any(i => i.Result.Definition.Classification == Classification.Experimental);
        if (experimental && !Dialogs.Confirm("Otimização experimental",
                "Você escolheu uma otimização EXPERIMENTAL. O resultado varia por PC e jogo e pode piorar o desempenho.\n\n" +
                "Meça com o FPSX Benchmark antes e depois, e desfaça pelo Histórico se não houver ganho.", "Entendo, continuar"))
            return null;

        var elevated = items.Where(i => i.Result.RequiresElevation).ToList();
        var confirm = Describe(items) + (elevated.Count > 0
            ? "\nO Windows vai pedir sua permissão para as alterações de sistema. Isso é normal: o FPSX usa a permissão só para elas e fecha em seguida."
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

    /// <summary>Só plano bloqueia: permissão de administrador é pedida na hora de aplicar.</summary>
    private static bool ResolveBlocked(IReadOnlyList<OptimizationResult> results)
    {
        var blocked = results.Where(r => r.Decision == Decision.Blocked).ToList();
        if (blocked.Count == 0)
            return true;

        var plan = blocked.First();
        if (Dialogs.Show("Disponível em outro plano", $"\"{plan.Definition.Name}\": {plan.Reason}", "Ver planos", "Fechar") == 0)
            AppHost.OpenUrl(AppHost.Current.SiteUrl + "/planos");
        return false;
    }

    private static string Describe(IEnumerable<(OptimizationResult Result, Fpsx.Core.Optimizations.Proposal Proposal)> items)
    {
        var sb = new StringBuilder("O FPSX vai criar um backup antes de cada alteração e conferir o resultado depois.\n");
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
            sb.Append("\n\nTudo pode ser desfeito na tela Histórico. Para medir o efeito no jogo, use o FPSX Benchmark.");
        return sb.ToString();
    }
}
