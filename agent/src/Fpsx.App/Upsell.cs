using Fpsx.Core.Engine;
using Fpsx.Core.Model;

namespace Fpsx.App;

/// <summary>
/// O convite para liberar uma correção, igual em todas as telas. Quem ainda
/// não tem conta vai para o teste grátis do Pro (sem cartão), que é a oferta
/// sem risco. Quem tem conta vai direto para o pagamento do plano que libera
/// aquilo, com o número de correções esperando, e não para a tabela de planos.
/// </summary>
public static class Upsell
{
    /// <summary>Plano mínimo que libera todas as otimizações travadas por plano, ou null se nenhuma está.</summary>
    public static string? RequiredPlan(ScanResult scan, IEnumerable<OptimizationResult> results) =>
        results.Where(r => IsPlanLocked(scan.Plan, r))
            .Select(r => r.Definition.MinPlan)
            .OrderByDescending(Plans.Rank)
            .FirstOrDefault();

    /// <summary>Travada pelo plano, e não por outro motivo (desativada pelo admin, por exemplo).</summary>
    public static bool IsPlanLocked(string plan, OptimizationResult r) =>
        r.Decision == Decision.Blocked && Plans.Rank(plan) < Plans.Rank(r.Definition.MinPlan);

    public static string CheckoutUrl(string plan, int waiting) =>
        $"{AppHost.Current.SiteUrl}/pagamento?plano={Uri.EscapeDataString(plan)}&origem=app&achados={waiting}";

    /// <summary>Texto do botão principal quando há correção travada.</summary>
    public static string ButtonLabel(string plan)
    {
        var l = AppHost.Current.License;
        if (!l.LoggedIn)
            return "Testar o Pro grátis";
        return l.Expired ? "Renovar plano" : $"Liberar no {Plans.Label(plan)}";
    }

    /// <summary>Abre o caminho certo: teste grátis sem conta, pagamento com conta.</summary>
    public static void Go(string plan, int waiting)
    {
        if (!AppHost.Current.License.LoggedIn)
            LoginPrompt.ShowNow();
        else
            AppHost.OpenUrl(CheckoutUrl(plan, waiting));
    }

    /// <summary>Diálogo de quem tentou aplicar algo travado.</summary>
    public static void Offer(string plan, int waiting, string message)
    {
        var l = AppHost.Current.License;
        var extra = l.LoggedIn
            ? $"\n\nO plano {Plans.Label(plan)} libera esta e as outras correções do diagnóstico. Tudo continua com backup e pode ser desfeito."
            : $"\n\nCrie sua conta grátis no app e teste o plano Pro, sem cartão: medição, ajuste dos jogos e até {TrialQuota.MaxOptimizations} correções. Tudo continua com backup e pode ser desfeito.";
        var title = !l.LoggedIn ? "Teste o Pro grátis" : l.Status == "trial" ? "Teste grátis" : "Disponível em outro plano";
        if (Dialogs.Show(title, message + extra, ButtonLabel(plan), "Agora não") == 0)
            Go(plan, waiting);
    }
}
