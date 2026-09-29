using Fpsx.Core.Catalog;
using Fpsx.Core.Model;

namespace Fpsx.Core.Optimizations;

public static class OptimizationRegistry
{
    /// <summary>Todos os handlers compilados. Adicionar aqui é a única forma de uma otimização existir.</summary>
    public static IReadOnlyList<IOptimization> All { get; } =
    [
        new GameModeOptimization(),
        new BackgroundRecordingOptimization(),
        new LeavePowerSaverOptimization(),
        new HighPerformancePowerOptimization(),
        new DisplayRefreshOptimization(),
        new StartupOptimization(),
        new HagsOptimization(),
        new DirectXShaderCacheClear(),
        new Cs2ShaderCacheClear(),
        new DnsFlush(),
        new WinsockReset(),
        new SystemFilesRepairOptimization(),
        new DriverUpdateOptimization(),
        new PagefileRestoreOptimization(),
        new BackgroundProcessCloseOptimization(),
        new TempCleanupOptimization(),
        new GameSettingsFixOptimization(),
        new GamePresetOptimization(),
        new GameGpuPreferenceOptimization(),
        new WindowedGamesOptimization(),
        new TransparencyOptimization(),
        new DiscordHardwareAccelerationOptimization(),
        new MouseAccelerationOptimization(),
        new GraphicsProfileOptimization(),
    ];
}

/// <summary>
/// Confere o catálogo contra os handlers antes de qualquer scan. Catálogo com
/// id desconhecido, handler sem catálogo ou justificativa incompleta é
/// recusado inteiro: é o "impedir módulos desconhecidos" do spec.
/// </summary>
public static class CatalogValidator
{
    public static IReadOnlyList<string> Validate(OptimizationCatalog catalog, IEnumerable<IOptimization> handlers)
    {
        var errors = new List<string>();
        var handlerIds = handlers.Select(h => h.Id).ToHashSet();
        var seen = new HashSet<string>();

        foreach (var def in catalog.Optimizations)
        {
            if (!seen.Add(def.Id))
                errors.Add($"id duplicado no catálogo: {def.Id}");

            // NOT_RECOMMENDED fica no catálogo para transparência ("por que não
            // aplicamos isso"), e por isso mesmo não pode ter handler.
            if (def.Classification == Classification.NotRecommended)
            {
                if (handlerIds.Contains(def.Id))
                    errors.Add($"{def.Id}: NOT_RECOMMENDED não pode ter handler executável");
            }
            else if (!handlerIds.Contains(def.Id))
            {
                errors.Add($"{def.Id}: está no catálogo mas não existe handler compilado");
            }

            foreach (var (field, value) in def.Justification.Answers())
                if (string.IsNullOrWhiteSpace(value))
                    errors.Add($"{def.Id}: justificativa sem resposta para '{field}'");
        }

        foreach (var id in handlerIds.Where(id => catalog.Find(id) is null))
            errors.Add($"{id}: handler compilado sem entrada no catálogo");

        foreach (var profile in catalog.Profiles)
            foreach (var id in profile.Include.Concat(profile.Exclude).Where(id => catalog.Find(id) is null))
                errors.Add($"perfil {profile.Id}: referencia otimização inexistente {id}");

        return errors;
    }
}
