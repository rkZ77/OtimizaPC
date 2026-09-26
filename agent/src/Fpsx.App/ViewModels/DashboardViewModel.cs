using System.Collections.ObjectModel;
using System.Windows.Input;
using Fpsx.Core.Model;

namespace Fpsx.App.ViewModels;

public abstract class PageViewModel : ObservableObject
{
    private bool _busy;
    private string _progress = "";

    public abstract string Title { get; }

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
        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppHost.Scan))
                Load();
        };
        Load();
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
            ScanInfo = $"Análise de {scan.Snapshot.CapturedAt.ToLocalTime():dd/MM HH:mm} · perfil {scan.ProfileId} · catálogo {scan.CatalogVersion}";
        }

        foreach (var name in new[] { nameof(HasScan), nameof(ProblemCount), nameof(RecommendedCount), nameof(OptimalCount), nameof(AutoCount), nameof(ScanInfo), nameof(Headline) })
            Raise(name);
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
