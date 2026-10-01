using Rkzfps.Core.Catalog;
using Rkzfps.Core.Model;

namespace Rkzfps.Client;

public sealed record CatalogOverride
{
    public string Kind { get; init; } = "";
    public string Id { get; init; } = "";
    public bool? Enabled { get; init; }
    public RiskLevel? Risk { get; init; }
    public string? Description { get; init; }
    public string? MinPlan { get; init; }
}

public sealed record OverridesPayload
{
    public int V { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
    public IReadOnlyList<CatalogOverride> Overrides { get; init; } = [];
}

/// <summary>
/// Aplica o que o admin mudou no painel ao catálogo local. Só metadado, só em
/// id que já existe, e só se a assinatura bater: o servidor desliga, muda
/// risco, texto ou plano mínimo, mas não cria otimização nem muda o que ela faz.
/// </summary>
public static class CatalogOverrides
{
    private static readonly HashSet<string> Plans = ["free", "starter", "pro", "ultimate"];

    public static OptimizationCatalog Apply(OptimizationCatalog catalog, IEnumerable<CatalogOverride> overrides)
    {
        var byId = overrides.Where(o => o.Kind == "optimization").GroupBy(o => o.Id).ToDictionary(g => g.Key, g => g.Last());
        var optimizations = catalog.Optimizations.Select(def =>
        {
            if (!byId.TryGetValue(def.Id, out var o))
                return def;
            return def with
            {
                Enabled = o.Enabled ?? def.Enabled,
                Risk = o.Risk ?? def.Risk,
                Description = string.IsNullOrWhiteSpace(o.Description) ? def.Description : o.Description,
                MinPlan = o.MinPlan is not null && Plans.Contains(o.MinPlan) ? o.MinPlan : def.MinPlan,
            };
        }).ToList();
        return catalog with { Optimizations = optimizations };
    }

    public static OptimizationCatalog ApplySigned(OptimizationCatalog catalog, string? token, string publicKeyPem = LicenseKeys.PublicKeyPem)
    {
        if (string.IsNullOrEmpty(token) || SignedToken.Verify<OverridesPayload>(token, publicKeyPem) is not { } payload)
            return catalog;
        return Apply(catalog, payload.Overrides);
    }
}
