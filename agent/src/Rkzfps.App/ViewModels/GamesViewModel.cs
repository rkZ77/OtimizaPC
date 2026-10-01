using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using Rkzfps.Core.Diagnostics;
using Rkzfps.Core.Engine;
using Rkzfps.Core.Model;

namespace Rkzfps.App.ViewModels;

public sealed class GameCard : ObservableObject
{
    private string _tips = "";

    /// <summary>Dicas de vídeo escritas pela IA para este jogo neste PC (a pedido).</summary>
    public string TipsText
    {
        get => _tips;
        set => Set(ref _tips, value);
    }

    public string GameId { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>Ícone do próprio jogo instalado; sem ele, a tela mostra <see cref="Initials"/>.</summary>
    public System.Windows.Media.ImageSource? Icon { get; init; }

    public string Initials => GameIcons.Initials(Name);
    public string Status { get; init; } = "";
    public string? InstallPath { get; init; }
    public bool Installed { get; init; }
    public IReadOnlyList<FindingItem> Findings { get; init; } = [];
    public IReadOnlyList<KeyValueItem> Recommended { get; init; } = [];
    public bool HasShaderCache { get; init; }

    /// <summary>Perfis de gráficos deste jogo (Máximo FPS, Equilibrado), com o ativo marcado.</summary>
    public IReadOnlyList<Rkzfps.Core.Engine.GraphicsProfileOption> Profiles { get; init; } = [];
    public bool HasProfiles => Profiles.Count > 0;

    // ---- Otimizar este jogo / Voltar como era ----

    /// <summary>O RKZFPS sabe editar a configuração deste jogo (CS2, Fortnite, Minecraft).</summary>
    public bool Tunable { get; init; }

    public string TuneTitle { get; init; } = "";
    public string TuneDetail { get; init; } = "";
    public Brush TuneBrush { get; init; } = Brushes.Transparent;
    public bool CanOptimize { get; init; }
    public bool CanRevert { get; init; }

    /// <summary>Jogo só com medição: o ajuste é pelo menu dele, com as dicas abaixo.</summary>
    public string ManualNote { get; init; } = "";
}

/// <summary>
/// Um cartão por jogo deste PC, com o que muda de verdade no FPS: otimizar
/// para o nível do hardware com um clique e voltar como era com outro.
/// </summary>
public sealed class GamesViewModel : PageViewModel
{
    private readonly AppHost _host = AppHost.Current;

    public GamesViewModel()
    {
        OptimizeGameCommand = new AsyncCommand(p => Busy(() => Optimize((string)p!)), p => !IsBusy && p is string);
        RevertGameCommand = new AsyncCommand(p => Busy(() => Revert((string)p!)), p => !IsBusy && p is string);
        ShaderCommand = new AsyncCommand(() => Busy(ClearShaders), () => !IsBusy);
        TipsCommand = new AsyncCommand(p => Tips((GameCard)p!), p => p is GameCard c && c.TipsText != Thinking);
        // Perfil escolhido pela pessoa: mesmo fluxo de aplicar (confirmação, backup, desfazer).
        ProfileCommand = new AsyncCommand(p => Busy(async () => { await ApplyFlow.RunAsync([(string)p!], Reporter); Load(); }),
            p => !IsBusy && p is string && CanFix);
        ScanCommand = new AsyncCommand(() => Busy(() => _host.RunScanAsync(Reporter, network: false)), () => !IsBusy);
        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppHost.Scan) or nameof(AppHost.License) or nameof(AppHost.Tuning))
                Load();
        };
        Load();
    }

    public override string Title => "Jogos";

    public override string Icon => "";

    public ICommand OptimizeGameCommand { get; }
    public ICommand RevertGameCommand { get; }
    public ICommand ShaderCommand { get; }
    public ICommand TipsCommand { get; }

    private const string Thinking = "Pensando no seu PC e nas suas partidas...";
    public ICommand ProfileCommand { get; }
    public ICommand ScanCommand { get; }

    public string OtherGames { get; private set; } = "";

    /// <summary>Nível do PC vindo do diagnóstico, para a pessoa entender o que cada jogo recebe.</summary>
    public string TierTitle { get; private set; } = "";
    public string TierDetail { get; private set; } = "";
    public ObservableCollection<GameCard> Games { get; } = [];
    public bool CanFix => _host.Allows(Feature.GameProfiles);
    public string PlanNote => CanFix ? "" : $"Otimizar os jogos com um clique faz parte do plano {Plans.Label(PlanFeatures.RequiredPlan(Feature.GameProfiles))}. Você já vê aqui o que mudaria em cada um.";

    public override void OnShown() => Load();

    private async Task Optimize(string gameId)
    {
        if (_host.Scan is not { } scan)
            return;
        var ids = GameTuning.ProposalIdsFor(scan, gameId, _host.Tuning);
        if (ids.Count > 0)
            await ApplyFlow.RunAsync(ids, Reporter);
        Load();
    }

    /// <summary>
    /// O que baixar primeiro no menu de vídeo deste jogo, pela IA, com o
    /// hardware, o nível do PC e o que foi medido nas partidas dele. Vale para
    /// qualquer jogo reconhecido, não só os que o RKZFPS ajusta sozinho.
    /// </summary>
    private async Task Tips(GameCard card)
    {
        card.TipsText = Thinking;
        try
        {
            var measured = Rkzfps.Core.Benchmark.UpgradeEvidence.Games(_host.Ctx.Gameplay.All()).FirstOrDefault(g => g.GameId == card.GameId);
            var tier = _host.Scan is { } scan ? HardwareTierClassifier.Assess(scan.Snapshot).Label : "";
            card.TipsText = await _host.Ctx.GameTipsAsync(card.Name, _host.Report?.Hardware ?? new Dictionary<string, string>(), tier, measured, _host.Tuning);
        }
        catch (Rkzfps.Client.ApiException ex)
        {
            card.TipsText = ex.Message;
        }
    }

    private async Task ClearShaders()
    {
        if (!Dialogs.Confirm("Limpar cache de shaders",
                "Use só se o jogo começou a travar de um jeito estranho ou mostrar erro gráfico depois de uma atualização. Não aumenta FPS. A primeira partida depois fica com mais travadas enquanto o jogo recria os shaders, e o RKZFPS deixa essa partida fora do antes e depois.",
                "Limpar mesmo assim"))
            return;
        await ApplyFlow.RunAsync(["shader-cache-clear-cs2"], Reporter);
    }

    private async Task Revert(string gameId)
    {
        var applied = GameTuning.AppliedChanges(_host.Ctx.Store.All(), gameId);
        if (applied.Count == 0)
            return;
        var name = _host.Ctx.GameProfiles.FirstOrDefault(p => p.Id == gameId)?.Name ?? gameId;
        if (!Dialogs.Confirm("Voltar como era", $"As {applied.Count} opções que o RKZFPS mudou no {name} voltam para o valor que tinham antes. Feche o jogo antes de continuar.", "Voltar como era"))
            return;
        foreach (var (sessionId, changeId, _) in applied)
            await _host.RollbackAsync(sessionId, changeId, force: false);
        await _host.RunScanAsync(Reporter, network: false);
        Dialogs.Info("Pronto", $"O {name} voltou à configuração que tinha antes do RKZFPS.");
    }

    private void Load()
    {
        Games.Clear();
        var scan = _host.Scan;
        var tierFinding = scan?.Findings.FirstOrDefault(f => f.DiagnosticId == "hardware-tier");
        TierTitle = tierFinding?.Title ?? "";
        TierDetail = tierFinding?.Detail ?? "";
        var tier = scan is null ? HardwareTier.Unknown : HardwareTierClassifier.Assess(scan.Snapshot).Tier;
        var tuning = _host.Tuning;
        var keepImage = tuning is { Legacy: false, GraphicsTradeoff: GraphicsTradeoff.None };
        var sessions = _host.Ctx.Store.All();

        // Só os jogos deste PC (instalados ou já jogados com o RKZFPS aberto):
        // 15 cartões de "não encontrado" esconderiam os que importam.
        var played = _host.Ctx.Gameplay.All().Select(s => s.GameId).ToHashSet();
        var mine = _host.Ctx.GameProfiles
            .Where(p => scan?.Snapshot.Games.Any(g => g.GameId == p.Id) == true || played.Contains(p.Id))
            .ToList();
        var others = _host.Ctx.GameProfiles.Except(mine).Select(p => p.Name).ToList();
        OtherGames = others.Count == 0 ? "" : "O RKZFPS também reconhece e mede o FPS de: " + string.Join(", ", others) + ". Eles aparecem aqui quando forem instalados ou jogados com o RKZFPS aberto.";

        var res = System.Windows.Application.Current.Resources;
        foreach (var profile in mine)
        {
            var install = scan?.Snapshot.Games.FirstOrDefault(g => g.GameId == profile.Id);
            var findings = scan is null || install is null
                ? []
                : scan.Findings.Where(f => f.DiagnosticId == "game-settings" && f.Title.StartsWith(profile.Name, StringComparison.Ordinal))
                    .Select(f => new FindingItem(f, scan)).ToList();
            var tunable = profile.Config is not null && install?.Config.Count > 0;
            var pending = scan is null || !tunable ? [] : GameTuning.ProposalIdsFor(scan, profile.Id, tuning);
            var applied = GameTuning.AppliedChanges(sessions, profile.Id);
            var changes = scan is null ? 0 : pending.Sum(id => scan.FindProposal(id)?.Proposal.Changes.Count ?? 0);

            string title, detail;
            Brush brush;
            if (!tunable)
            {
                title = "";
                detail = "";
                brush = Brushes.Transparent;
            }
            else if (pending.Count > 0)
            {
                title = applied.Count > 0 ? "Otimizado, com ajustes novos para aplicar" : "Pode ficar melhor neste PC";
                detail = $"{changes} {(changes == 1 ? "opção" : "opções")} para ajustar: correções que tiram atraso"
                         + (keepImage && tier is HardwareTier.Low or HardwareTier.Mid
                             ? ". A imagem do jogo não muda, como você pediu."
                             : tier switch
                         {
                             HardwareTier.Low => " e a configuração leve para PC de entrada.",
                             HardwareTier.Mid => " e a configuração equilibrada para PC intermediário.",
                             _ => ". Em PC forte a qualidade de imagem não muda.",
                         });
                brush = (Brush)res["Warn"];
            }
            else if (applied.Count > 0)
            {
                title = "Otimizado pelo RKZFPS";
                detail = $"{applied.Count} {(applied.Count == 1 ? "opção ajustada" : "opções ajustadas")} em {applied[0].At.ToLocalTime():dd/MM}. Pode voltar como era quando quiser.";
                brush = (Brush)res["Accent"];
            }
            else
            {
                title = "Já está na configuração certa para este PC";
                detail = "Nada para mudar neste jogo agora.";
                brush = (Brush)res["Accent"];
            }

            Games.Add(new GameCard
            {
                GameId = profile.Id,
                Name = profile.Name,
                Icon = GameIcons.Get(profile.Id, install?.ExecutablePath),
                Installed = install is not null,
                InstallPath = install?.InstallPath,
                Status = install is not null ? "Instalado."
                    : played.Contains(profile.Id) ? "Jogado neste PC: o FPS das partidas está em Partidas e FPS."
                    : "Rode a análise para detectar o jogo.",
                Findings = findings,
                Recommended = profile.RecommendedSettings.Select(kv => new KeyValueItem(kv.Key, kv.Value)).ToList(),
                HasShaderCache = install is not null && profile.Optimizations.Contains("shader-cache-clear-cs2"),
                Profiles = scan is null ? [] : Rkzfps.Core.Engine.GameTuning.Profiles(scan, profile),
                Tunable = tunable,
                TuneTitle = title,
                TuneDetail = detail,
                TuneBrush = brush,
                CanOptimize = pending.Count > 0,
                CanRevert = applied.Count > 0,
                ManualNote = tunable ? "" : profile.Config is null
                    ? "Neste jogo o RKZFPS mede o FPS e mostra o que mais pesa. O ajuste é pelo menu de vídeo do próprio jogo, com as dicas abaixo."
                    : "Abra o jogo uma vez para ele criar o arquivo de configuração. Depois o RKZFPS consegue otimizar.",
            });
        }

        foreach (var n in new[] { nameof(CanFix), nameof(PlanNote), nameof(TierTitle), nameof(TierDetail), nameof(OtherGames) })
            Raise(n);
    }
}
