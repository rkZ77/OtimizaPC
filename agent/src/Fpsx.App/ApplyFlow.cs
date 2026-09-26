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

        if (!Dialogs.Confirm("Confirmar alterações", Describe(items), "Aplicar com backup"))
            return null;

        var session = await host.ApplyAsync(items.Select(i => i.Proposal.Id).ToList(), experimental, progress);
        Dialogs.Info("Resultado", Summary(session));
        return session;
    }

    /// <summary>Explica o bloqueio e oferece a saída: reabrir como admin ou ver planos.</summary>
    private static bool ResolveBlocked(IReadOnlyList<OptimizationResult> results)
    {
        var blocked = results.Where(r => r.Decision == Decision.Blocked).ToList();
        if (blocked.Count == 0)
            return true;

        var admin = blocked.FirstOrDefault(b => b.Reason.Contains("administrador"));
        if (admin is not null)
        {
            if (Dialogs.Show("Permissão de administrador",
                    $"\"{admin.Definition.Name}\" altera uma configuração do sistema e exige abrir o FPSX como administrador.",
                    "Reabrir como administrador", "Agora não") == 0 && AppHost.RelaunchAsAdmin())
                System.Windows.Application.Current.Shutdown();
            return false;
        }

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
