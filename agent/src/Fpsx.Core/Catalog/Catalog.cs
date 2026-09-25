using System.Text.Json;
using Fpsx.Core.Json;
using Fpsx.Core.Model;

namespace Fpsx.Core.Catalog;

/// <summary>
/// As dez perguntas da seção 51 do spec. Otimização sem todas respondidas não
/// entra no catálogo: o CatalogValidator recusa o arquivo inteiro.
/// </summary>
public sealed record Justification
{
    public string Problem { get; init; } = "";
    public string WorksOn { get; init; } = "";
    public string DoesNotWorkOn { get; init; } = "";
    public string ExpectedBenefit { get; init; } = "";
    public string HowToMeasure { get; init; } = "";
    public string Risk { get; init; } = "";
    public string Rollback { get; init; } = "";
    public string Evidence { get; init; } = "";
    public string SaferAlternative { get; init; } = "";
    public string UserNeed { get; init; } = "";

    public IEnumerable<(string Field, string Value)> Answers()
    {
        yield return ("problem", Problem);
        yield return ("works_on", WorksOn);
        yield return ("does_not_work_on", DoesNotWorkOn);
        yield return ("expected_benefit", ExpectedBenefit);
        yield return ("how_to_measure", HowToMeasure);
        yield return ("risk", Risk);
        yield return ("rollback", Rollback);
        yield return ("evidence", Evidence);
        yield return ("safer_alternative", SaferAlternative);
        yield return ("user_need", UserNeed);
    }
}

public sealed record OptimizationDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public Classification Classification { get; init; }
    public RiskLevel Risk { get; init; }

    /// <summary>Área onde o efeito aparece: FPS, LATENCY, LOADING, STUTTER, DISPLAY, NETWORK, SYSTEM.</summary>
    public string ImpactArea { get; init; } = "";

    public string Description { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public bool RequiresUserConfirmation { get; init; }
    public bool Measurement { get; init; }

    /// <summary>Plano mínimo que libera a aplicação (free, starter, pro, ultimate).</summary>
    public string MinPlan { get; init; } = "free";

    public Justification Justification { get; init; } = new();
}

public sealed record DiagnosticDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public bool Enabled { get; init; } = true;
}

public sealed record ProfileDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>Ids que o perfil pode selecionar automaticamente. Vazio = todos do catálogo.</summary>
    public IReadOnlyList<string> Include { get; init; } = [];

    public IReadOnlyList<string> Exclude { get; init; } = [];
    public IReadOnlyList<Classification> AutoClassifications { get; init; } = [Classification.Proven, Classification.Conditional];
    public RiskLevel MaxRisk { get; init; } = RiskLevel.Low;
}

public sealed record OptimizationCatalog
{
    public string Version { get; init; } = "";
    public IReadOnlyList<OptimizationDefinition> Optimizations { get; init; } = [];
    public IReadOnlyList<DiagnosticDefinition> Diagnostics { get; init; } = [];
    public IReadOnlyList<ProfileDefinition> Profiles { get; init; } = [];

    public OptimizationDefinition? Find(string id) => Optimizations.FirstOrDefault(o => o.Id == id);

    public ProfileDefinition? FindProfile(string id) =>
        Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public static OptimizationCatalog Load(string directory)
    {
        var optimizations = Read<OptimizationCatalog>(Path.Combine(directory, "optimizations.json"));
        var profiles = Read<ProfilesFile>(Path.Combine(directory, "profiles.json"));
        return optimizations with { Profiles = profiles.Profiles };
    }

    private static T Read<T>(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Arquivo do catálogo não encontrado: {path}", path);
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), FpsxJson.Options)
               ?? throw new InvalidDataException($"Catálogo vazio: {path}");
    }

    private sealed record ProfilesFile
    {
        public IReadOnlyList<ProfileDefinition> Profiles { get; init; } = [];
    }
}
