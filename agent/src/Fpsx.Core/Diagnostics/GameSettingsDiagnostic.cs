using System.Globalization;
using Fpsx.Core.Games;
using Fpsx.Core.Model;
using Fpsx.Core.Optimizations;

namespace Fpsx.Core.Diagnostics;

/// <summary>
/// Avalia a configuração salva de cada jogo detectado contra as regras do
/// perfil JSON. Genérico de propósito: jogo novo = perfil novo, sem código.
/// </summary>
public sealed class GameSettingsDiagnostic : IDiagnostic
{
    public string Id => "game-settings";

    public IEnumerable<Finding> Run(EvaluationContext context)
    {
        foreach (var profile in context.GameProfiles)
        {
            var game = context.Game(profile.Id);
            if (game is null)
                continue;

            if (game.ConfigPath is null || game.Config.Count == 0)
            {
                yield return new Finding
                {
                    DiagnosticId = Id, Area = Areas.Game, Status = HealthStatus.Info,
                    Title = $"{profile.Name}: configuração não encontrada",
                    Detail = "O jogo está instalado, mas o arquivo de configuração de vídeo ainda não existe. Abra o jogo uma vez e rode o scan de novo.",
                    Evidence = new Dictionary<string, string> { ["instalacao"] = game.InstallPath },
                };
                continue;
            }

            var ok = true;
            foreach (var finding in CheckSettings(profile, game, context.Snapshot))
            {
                ok = false;
                yield return finding;
            }

            if (RefreshRate(profile, game, context.Snapshot) is { } refresh)
            {
                ok = false;
                yield return refresh;
            }

            if (ok)
            {
                yield return new Finding
                {
                    DiagnosticId = Id, Area = Areas.Game, Status = HealthStatus.Ok,
                    Title = $"{profile.Name}: configuração sem problemas",
                    Detail = "As opções de vídeo que afetam latência e desempenho já estão adequadas.",
                    Evidence = new Dictionary<string, string> { ["arquivo"] = game.ConfigPath },
                };
            }
        }
    }

    private IEnumerable<Finding> CheckSettings(GameProfile profile, GameInstall game, SystemSnapshot snapshot)
    {
        var vendors = snapshot.Gpus.Where(g => !g.LikelyIntegrated).Select(g => g.Vendor).ToHashSet();
        foreach (var check in profile.SettingChecks)
        {
            if (check.GpuVendor is { } vendor && !vendors.Contains(vendor))
                continue;
            // Chave ausente não é problema: o jogo usa o padrão dele, que não conhecemos.
            if (!game.Config.TryGetValue(check.Key, out var value))
                continue;
            if (check.ExpectAny.Contains(value, StringComparer.OrdinalIgnoreCase))
                continue;

            yield return new Finding
            {
                DiagnosticId = Id, Area = Areas.Game, Status = check.Severity, ImpactArea = "LATENCY",
                FixOptimizationId = check.FixValue is null ? null : "game-settings-fix",
                Title = $"{profile.Name}: {check.Title}",
                Detail = check.Why,
                Recommendation = check.Recommendation,
                Evidence = new Dictionary<string, string> { [check.Key] = value, ["esperado"] = string.Join(" ou ", check.ExpectAny) },
            };
        }
    }

    /// <summary>Jogo configurado a 60 Hz num monitor de 144 Hz trava o FPS exibido, mesmo com o Windows certo.</summary>
    private Finding? RefreshRate(GameProfile profile, GameInstall game, SystemSnapshot snapshot)
    {
        var rule = profile.RefreshRateCheck;
        var primary = snapshot.Displays.FirstOrDefault(d => d.IsPrimary) ?? snapshot.Displays.FirstOrDefault();
        if (rule is null || primary is null)
            return null;
        if (!TryInt(game.Config, rule.NumeratorKey, out var num) || !TryInt(game.Config, rule.DenominatorKey, out var den) || den <= 0)
            return null;

        var gameHz = (int)Math.Round((double)num / den);
        if (gameHz + 1 >= primary.MaxHzAtCurrentResolution)
            return null;

        return new Finding
        {
            DiagnosticId = Id, Area = Areas.Game, Status = HealthStatus.Attention, ImpactArea = "DISPLAY",
            FixOptimizationId = "game-settings-fix",
            Title = $"{profile.Name}: taxa de atualização do jogo abaixo do monitor",
            Detail = $"O jogo está configurado para {gameHz} Hz e o monitor suporta {primary.MaxHzAtCurrentResolution} Hz. Em tela cheia o jogo usa a taxa dele, e a tela mostra menos quadros do que poderia.",
            Recommendation = $"No jogo: Configurações > Vídeo > Taxa de atualização: {primary.MaxHzAtCurrentResolution} Hz.",
            Evidence = new Dictionary<string, string> { ["jogo"] = $"{gameHz} Hz", ["monitor"] = $"{primary.MaxHzAtCurrentResolution} Hz" },
        };
    }

    private static bool TryInt(IReadOnlyDictionary<string, string> config, string key, out int value)
    {
        value = 0;
        return config.TryGetValue(key, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}

public static class DiagnosticRegistry
{
    public static IReadOnlyList<IDiagnostic> All { get; } =
    [
        new WindowsVersionDiagnostic(),
        new CpuThrottlingDiagnostic(),
        new BackgroundLoadDiagnostic(),
        new MemoryDiagnostic(),
        new StorageDiagnostic(),
        new GpuDriverDiagnostic(),
        new NetworkDiagnostic(),
        new MemoryIntegrityDiagnostic(),
        new GameSettingsDiagnostic(),
    ];
}
