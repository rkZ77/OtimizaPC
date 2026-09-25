namespace Fpsx.Core.Engine;

public enum Feature
{
    /// <summary>Scan completo e relatório de diagnóstico.</summary>
    Scan,

    /// <summary>Correções comprovadas de configuração (Game Mode, energia, monitor).</summary>
    BasicFixes,

    /// <summary>Desfazer alterações. Liberado em TODOS os planos: nunca se cobra para desfazer.</summary>
    Rollback,

    /// <summary>Resolver problemas encontrados (processos, pagefile, espaço em disco).</summary>
    ProblemFixes,

    Startup,
    Troubleshooting,
    History,

    /// <summary>Perfis de jogo: diagnóstico e correção das configurações do jogo.</summary>
    GameProfiles,

    Benchmark,
    Reports,
    Experimental,
}

/// <summary>
/// Matriz única do que cada plano libera, do básico ao que contém tudo. O app,
/// o CLI e o site (via /api/public/plans) mostram a mesma divisão. Cada
/// otimização ainda tem seu min_plan no catálogo; o teste de consistência
/// garante que os dois não se contradizem.
/// </summary>
public static class PlanFeatures
{
    private static readonly Dictionary<Feature, string> MinPlan = new()
    {
        [Feature.Scan] = "free",
        [Feature.BasicFixes] = "free",
        [Feature.Rollback] = "free",
        [Feature.ProblemFixes] = "starter",
        [Feature.Startup] = "starter",
        [Feature.Troubleshooting] = "starter",
        [Feature.History] = "starter",
        [Feature.GameProfiles] = "pro",
        [Feature.Benchmark] = "pro",
        [Feature.Reports] = "pro",
        [Feature.Experimental] = "ultimate",
    };

    public static string RequiredPlan(Feature feature) => MinPlan[feature];

    public static bool Allows(string plan, Feature feature) => Plans.Rank(plan) >= Plans.Rank(MinPlan[feature]);

    public static IEnumerable<Feature> For(string plan) => MinPlan.Keys.Where(f => Allows(plan, f));

    public static string Label(Feature f) => f switch
    {
        Feature.Scan => "Scan completo do PC",
        Feature.BasicFixes => "Correções básicas comprovadas",
        Feature.Rollback => "Desfazer qualquer alteração",
        Feature.ProblemFixes => "Resolver problemas encontrados",
        Feature.Startup => "Programas de inicialização",
        Feature.Troubleshooting => "Ferramentas de troubleshooting",
        Feature.History => "Histórico de otimizações",
        Feature.GameProfiles => "Perfis de jogo (CS2)",
        Feature.Benchmark => "FPSX Benchmark",
        Feature.Reports => "Relatórios",
        _ => "Otimizações experimentais",
    };
}
