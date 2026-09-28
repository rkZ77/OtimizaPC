using System.Collections.ObjectModel;
using System.Windows.Input;
using Fpsx.Core.Model;

namespace Fpsx.App.ViewModels;

public abstract class PageViewModel : ObservableObject
{
    private bool _busy;
    private string _progress = "";

    public abstract string Title { get; }

    /// <summary>Ícone do menu (Segoe MDL2 Assets, que vem com o Windows 10 e 11).</summary>
    public virtual string Icon => "";

    /// <summary>Só aparece no modo avançado.</summary>
    public virtual bool AdvancedOnly => false;

    private bool _isCurrent;

    /// <summary>Tela aberta agora: marca o item do menu, mesmo quando outra tela navegou até aqui.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set => Set(ref _isCurrent, value);
    }

    public bool IsBusy
    {
        get => _busy;
        protected set => Set(ref _busy, value);
    }

    public string Progress
    {
        get => _progress;
        protected set => Set(ref _progress, value);
    }

    protected IProgress<string> Reporter => new Progress<string>(s => Progress = s + "...");

    protected async Task Busy(Func<Task> work)
    {
        IsBusy = true;
        try
        {
            await work();
        }
        finally
        {
            IsBusy = false;
            Progress = "";
        }
    }

    public virtual void OnShown()
    {
    }
}

/// <summary>Início: "Seu PC", Performance Readiness, problemas com Resolver e Otimizar agora.</summary>
public sealed class DashboardViewModel : PageViewModel
{
    private readonly AppHost _host = AppHost.Current;

    public DashboardViewModel()
    {
        ScanCommand = new AsyncCommand(() => Busy(() => _host.RunScanAsync(Reporter)), () => !IsBusy);
        CpuTestCommand = new AsyncCommand(() => Busy(() => _host.RunScanAsync(Reporter, cpuTest: true)), () => !IsBusy);
        OptimizeCommand = new AsyncCommand(() => Busy(Optimize), () => !IsBusy && AutoCount > 0);
        FixCommand = new AsyncCommand(p => Busy(() => Fix((FindingItem)p!)), p => !IsBusy && p is FindingItem);
        OpenUrlCommand = new RelayCommand(p => AppHost.OpenUrl((string)p!), p => p is string);
        HeroCommand = new AsyncCommand(Hero, () => !IsBusy);
        GoGamesCommand = new RelayCommand(() => _host.Navigate<GamesViewModel>());
        GoGameplayCommand = new RelayCommand(() => _host.Navigate<GameplayViewModel>());
        GoHistoryCommand = new RelayCommand(() => _host.Navigate<HistoryViewModel>());
        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppHost.Scan) or nameof(AppHost.License))
                Load();
            if (e.PropertyName == nameof(AppHost.AdvancedMode))
                Raise(nameof(IsAdvanced));
        };
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IsBusy) or nameof(Progress))
                RaiseHero();
        };
        Load();
    }

    public ICommand HeroCommand { get; }
    public ICommand GoGamesCommand { get; }
    public ICommand GoGameplayCommand { get; }
    public ICommand GoHistoryCommand { get; }
    public bool IsAdvanced => _host.AdvancedMode;

    /// <summary>Otimizações que resolveriam algo mas pedem plano (no Free).</summary>
    public int BlockedCount { get; private set; }

    // Cartão principal: a situação do PC numa frase e UM botão. É o que o
    // usuário leigo precisa; os números ficam no modo avançado.
    private enum HeroState { Scanning, Problem, CanFix, NeedsPlan, Good }

    private HeroState State => IsBusy || !HasScan ? HeroState.Scanning
        : ProblemCount > 0 && AutoCount == 0 && BlockedCount == 0 ? HeroState.Problem
        : AutoCount > 0 ? HeroState.CanFix
        : BlockedCount > 0 ? HeroState.NeedsPlan
        : ProblemCount > 0 ? HeroState.Problem
        : HeroState.Good;

    public string HeroGlyph => State switch
    {
        HeroState.Scanning => "",
        HeroState.Good => "",
        HeroState.Problem => "",
        _ => "",
    };

    public System.Windows.Media.Brush HeroBrush => (System.Windows.Media.Brush)System.Windows.Application.Current.Resources[State switch
    {
        HeroState.Good => "Accent",
        HeroState.Problem => "Warn",
        HeroState.Scanning => "Info",
        _ => "Accent",
    }];

    public string HeroTitle => State switch
    {
        HeroState.Scanning => "Analisando o seu PC",
        HeroState.Good => "Seu PC está bem configurado",
        HeroState.CanFix => AutoCount == 1 ? "Encontramos 1 coisa para melhorar" : $"Encontramos {AutoCount} coisas para melhorar",
        HeroState.NeedsPlan => BlockedCount == 1 ? "1 coisa pode melhorar no seu PC" : $"{BlockedCount} coisas podem melhorar no seu PC",
        _ => ProblemCount == 1 ? "Encontramos 1 problema que precisa de você" : $"Encontramos {ProblemCount} problemas que precisam de você",
    };

    public string HeroText => State switch
    {
        HeroState.Scanning => Progress.Length > 0 ? Progress : "Só leitura: nada muda no PC nesta etapa.",
        HeroState.Good => "Nada para mudar agora. Jogue com o RKZFPS aberto para medir o FPS das suas partidas.",
        HeroState.CanFix => "O RKZFPS guarda como estava antes de mudar qualquer coisa. Tudo pode ser desfeito.",
        HeroState.NeedsPlan => "Veja abaixo o que cada uma resolve. Para o RKZFPS corrigir, entre com um plano na tela Conta.",
        _ => "Veja abaixo o que foi encontrado e o botão para resolver cada um.",
    };

    public string HeroButton => State switch
    {
        HeroState.Scanning => "Analisando...",
        HeroState.CanFix => "Corrigir agora",
        HeroState.NeedsPlan => "Ver planos",
        _ => "Otimizar de novo",
    };

    private async Task Hero()
    {
        switch (State)
        {
            case HeroState.CanFix:
                await Busy(Optimize);
                break;
            case HeroState.NeedsPlan:
                AppHost.OpenUrl(_host.SiteUrl + "/planos");
                break;
            default:
                // Windows, driver e jogo mudam com as atualizações: analisa de
                // novo e, se surgiu algo que o RKZFPS corrige, já corrige (com a
                // mesma confirmação de sempre). Sem nada novo, diz isso.
                await Busy(() => _host.RunScanAsync(Reporter));
                if (State == HeroState.CanFix)
                    await Busy(Optimize);
                else if (State == HeroState.Good)
                    Dialogs.Info("Tudo certo", "Analisamos de novo: nada mudou desde a última vez e o seu PC continua bem configurado.");
                break;
        }
    }

    private void RaiseHero()
    {
        foreach (var n in new[] { nameof(HeroGlyph), nameof(HeroBrush), nameof(HeroTitle), nameof(HeroText), nameof(HeroButton) })
            Raise(n);
    }

    public override string Title => "Início";

    public ICommand ScanCommand { get; }
    public ICommand CpuTestCommand { get; }
    public ICommand OptimizeCommand { get; }
    public ICommand FixCommand { get; }
    public ICommand OpenUrlCommand { get; }

    public bool HasScan => _host.Scan is not null;
    public ObservableCollection<KeyValueItem> Hardware { get; } = [];
    public ObservableCollection<ReadinessItem> Readiness { get; } = [];
    public ObservableCollection<FindingItem> Problems { get; } = [];
    public int ProblemCount { get; private set; }
    public int RecommendedCount { get; private set; }
    public int OptimalCount { get; private set; }
    public int AutoCount { get; private set; }
    public string ScanInfo { get; private set; } = "Nenhuma análise feita ainda.";

    public string Headline => !HasScan
        ? "Descubra o que realmente pode melhorar no seu PC."
        : ProblemCount == 0 && RecommendedCount == 0
            ? "Seu PC já está bem configurado. Nenhuma alteração necessária."
            : $"Encontramos {ProblemCount} ponto(s) de atenção e {RecommendedCount} otimização(ões) recomendada(s).";

    private void Load()
    {
        Hardware.Clear();
        Readiness.Clear();
        Problems.Clear();
        var scan = _host.Scan;
        var report = _host.Report;
        if (scan is not null && report is not null)
        {
            foreach (var (k, v) in report.Hardware)
                Hardware.Add(new KeyValueItem(k, v));
            foreach (var r in report.Readiness)
                Readiness.Add(new ReadinessItem(r.Area, r.Status, r.Summary));
            foreach (var f in scan.Findings.Where(f => f.Status is HealthStatus.Problem or HealthStatus.Attention)
                         .OrderBy(f => f.Status == HealthStatus.Problem ? 0 : 1))
                Problems.Add(new FindingItem(f, scan));

            ProblemCount = report.ProblemsFound;
            RecommendedCount = report.Recommended;
            OptimalCount = report.AlreadyOptimal;
            AutoCount = scan.Optimizations.Count(o => o.AutoSelected);
            BlockedCount = scan.Optimizations.Count(o => o.Decision == Decision.Blocked && o.Evaluation.Decision == Decision.Recommended);
            ScanInfo = $"Análise de {scan.Snapshot.CapturedAt.ToLocalTime():dd/MM HH:mm}, perfil {scan.ProfileId}, catálogo {scan.CatalogVersion}";
        }

        foreach (var name in new[] { nameof(HasScan), nameof(ProblemCount), nameof(RecommendedCount), nameof(OptimalCount), nameof(AutoCount), nameof(BlockedCount), nameof(ScanInfo), nameof(Headline) })
            Raise(name);
        RaiseHero();
    }

    private async Task Optimize()
    {
        var ids = _host.Scan!.Optimizations.Where(o => o.AutoSelected).Select(o => o.Definition.Id).ToList();
        await ApplyFlow.RunAsync(ids, Reporter);
    }

    private async Task Fix(FindingItem item)
    {
        if (item.FixId is { } id)
            await ApplyFlow.RunAsync([id], Reporter);
    }
}
