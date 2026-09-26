using System.Collections.ObjectModel;
using System.Windows.Input;
using Fpsx.Core.Diagnostics;
using Fpsx.Core.Engine;

namespace Fpsx.App.ViewModels;

public sealed class GameCard
{
    public string Name { get; init; } = "";
    public string Status { get; init; } = "";
    public string? InstallPath { get; init; }
    public bool Installed { get; init; }
    public IReadOnlyList<FindingItem> Findings { get; init; } = [];
    public IReadOnlyList<KeyValueItem> Recommended { get; init; } = [];
    public bool HasFix { get; init; }

    /// <summary>Proposta do preset leve; só preenchida quando o plano libera a aplicação.</summary>
    public string? PresetId { get; init; }
    public string? PresetTitle { get; init; }
    public string? PresetDescription { get; init; }
    public bool HasShaderCache { get; init; }
}

/// <summary>Perfis de jogo (CS2 primeiro): diagnóstico sempre visível, correção no plano Pro.</summary>
public sealed class GamesViewModel : PageViewModel
{
    private readonly AppHost _host = AppHost.Current;

    public GamesViewModel()
    {
        FixCommand = new AsyncCommand(() => Busy(() => ApplyFlow.RunAsync(["game-settings-fix"], Reporter)), () => !IsBusy);
        ShaderCommand = new AsyncCommand(() => Busy(() => ApplyFlow.RunAsync(["shader-cache-clear-cs2"], Reporter)), () => !IsBusy);
        PresetCommand = new AsyncCommand(p => Busy(() => ApplyFlow.RunAsync([(string)p!], Reporter)), p => !IsBusy && p is string);
        ScanCommand = new AsyncCommand(() => Busy(() => _host.RunScanAsync(Reporter, network: false)), () => !IsBusy);
        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppHost.Scan) or nameof(AppHost.License))
                Load();
        };
        Load();
    }

    public override string Title => "Jogos";

    public ICommand FixCommand { get; }
    public ICommand ShaderCommand { get; }
    public ICommand PresetCommand { get; }

    /// <summary>Nível do PC vindo do diagnóstico, para a pessoa entender por que o preset aparece (ou não).</summary>
    public string OtherGames { get; private set; } = "";

    public string TierTitle { get; private set; } = "";
    public string TierDetail { get; private set; } = "";
    public ICommand ScanCommand { get; }
    public ObservableCollection<GameCard> Games { get; } = [];
    public bool CanFix => _host.Allows(Feature.GameProfiles);
    public string PlanNote => CanFix ? "" : $"A correção automática das configurações do jogo faz parte do plano {Plans.Label(PlanFeatures.RequiredPlan(Feature.GameProfiles))}. O diagnóstico continua disponível.";

    private void Load()
    {
        Games.Clear();
        var scan = _host.Scan;
        var tier = scan?.Findings.FirstOrDefault(f => f.DiagnosticId == "hardware-tier");
        TierTitle = tier?.Title ?? "";
        TierDetail = tier is null ? "" : $"{tier.Detail} {tier.Recommendation}".Trim();
        var presets = scan?.Optimizations.FirstOrDefault(o => o.Definition.Id == "game-preset-low-end")?.Evaluation.Proposals ?? [];

        // Só os jogos deste PC (instalados ou já jogados com o FPSX aberto):
        // 15 cartões de "não encontrado" esconderiam os que importam.
        var played = _host.Ctx.Gameplay.All().Select(s => s.GameId).ToHashSet();
        var mine = _host.Ctx.GameProfiles
            .Where(p => scan?.Snapshot.Games.Any(g => g.GameId == p.Id) == true || played.Contains(p.Id))
            .ToList();
        var others = _host.Ctx.GameProfiles.Except(mine).Select(p => p.Name).ToList();
        OtherGames = others.Count == 0 ? "" : "O FPSX também reconhece e mede o FPS de: " + string.Join(", ", others) + ". Eles aparecem aqui quando forem instalados ou jogados com o FPSX aberto.";
        Raise(nameof(OtherGames));

        foreach (var profile in mine)
        {
            var preset = presets.FirstOrDefault(p => p.Id.StartsWith($"game-preset-low-end:{profile.Id}:", StringComparison.Ordinal));
            var install = scan?.Snapshot.Games.FirstOrDefault(g => g.GameId == profile.Id);
            var findings = scan is null || install is null
                ? []
                : scan.Findings.Where(f => f.DiagnosticId == "game-settings" && f.Title.StartsWith(profile.Name, StringComparison.Ordinal))
                    .Select(f => new FindingItem(f, scan)).ToList();
            Games.Add(new GameCard
            {
                Name = profile.Name,
                Installed = install is not null,
                InstallPath = install?.InstallPath,
                Status = install is not null ? "Instalado."
                    : played.Contains(profile.Id) ? "Jogado neste PC: o FPS das partidas está em Partidas."
                    : "Rode a análise para detectar o jogo.",
                Findings = findings,
                Recommended = profile.RecommendedSettings.Select(kv => new KeyValueItem(kv.Key, kv.Value)).ToList(),
                HasFix = findings.Any(f => f.HasFix),
                PresetId = preset is not null && CanFix ? preset.Id : null,
                PresetTitle = preset is null ? null
                    : $"Configuração leve para PC fraco: {preset.Changes.Count} {(preset.Changes.Count == 1 ? "opção está" : "opções estão")} mais pesada{(preset.Changes.Count == 1 ? "" : "s")} do que este PC aguenta",
                PresetDescription = preset is null ? null : CanFix ? preset.Rationale : $"{preset.Rationale} Disponível no plano {Plans.Label(PlanFeatures.RequiredPlan(Feature.GameProfiles))}.",
                HasShaderCache = profile.Optimizations.Contains("shader-cache-clear-cs2"),
            });
        }

        Raise(nameof(CanFix));
        Raise(nameof(PlanNote));
        Raise(nameof(TierTitle));
        Raise(nameof(TierDetail));
    }
}
