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
}

/// <summary>Perfis de jogo (CS2 primeiro): diagnóstico sempre visível, correção no plano Pro.</summary>
public sealed class GamesViewModel : PageViewModel
{
    private readonly AppHost _host = AppHost.Current;

    public GamesViewModel()
    {
        FixCommand = new AsyncCommand(() => Busy(() => ApplyFlow.RunAsync(["game-settings-fix"], Reporter)), () => !IsBusy);
        ShaderCommand = new AsyncCommand(() => Busy(() => ApplyFlow.RunAsync(["shader-cache-clear-cs2"], Reporter)), () => !IsBusy);
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
    public ICommand ScanCommand { get; }
    public ObservableCollection<GameCard> Games { get; } = [];
    public bool CanFix => _host.Allows(Feature.GameProfiles);
    public string PlanNote => CanFix ? "" : $"A correção automática das configurações do jogo faz parte do plano {PlanFeatures.RequiredPlan(Feature.GameProfiles)}. O diagnóstico continua disponível.";

    private void Load()
    {
        Games.Clear();
        var scan = _host.Scan;
        foreach (var profile in _host.Ctx.GameProfiles)
        {
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
                Status = scan is null ? "Rode a análise para detectar o jogo." : install is null ? "Não encontrado neste PC." : "Instalado.",
                Findings = findings,
                Recommended = profile.RecommendedSettings.Select(kv => new KeyValueItem(kv.Key, kv.Value)).ToList(),
                HasFix = findings.Any(f => f.HasFix),
            });
        }

        Raise(nameof(CanFix));
        Raise(nameof(PlanNote));
    }
}
