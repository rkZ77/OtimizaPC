using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using Fpsx.Core.Benchmark;
using Fpsx.Core.Engine;

namespace Fpsx.App.ViewModels;

public sealed record GameplayItem(GameplaySession Session)
{
    public string Title => $"{Session.StartedAt.ToLocalTime():dd/MM HH:mm}  {Session.GameName}";

    public string Stats => string.Format(CultureInfo.GetCultureInfo("pt-BR"),
        "FPS médio {0:0}, 1% low {1:0}, {2:0.#} travadas por minuto, {3:0} min medidos",
        Session.Stats.AvgFps, Session.Stats.Low1Fps, Session.Stats.StuttersPerMinute, Session.MeasuredSeconds / 60);
}

public sealed record PivotItem(SessionRecord Session)
{
    public string Label
    {
        get
        {
            var applied = Session.Changes.Where(c => c.Status == ChangeStatus.Applied).ToList();
            var first = applied.First().Applied.Describe();
            return $"{Session.StartedAt.ToLocalTime():dd/MM HH:mm}: {first}{(applied.Count > 1 ? $" e mais {applied.Count - 1}" : "")}";
        }
    }
}

public sealed record MetricItem(string Name, string Before, string After, string Delta, Brush Tone);

/// <summary>
/// Partidas medidas sozinhas pelo monitor e o antes e depois de uma
/// otimização. Só números medidos; sem partidas suficientes, a tela diz o
/// que falta em vez de inventar resultado.
/// </summary>
public sealed class GameplayViewModel : PageViewModel
{
    private readonly AppHost _host = AppHost.Current;
    private PivotItem? _pivot;
    private string? _game;
    private string _compareStatus = "";
    private string _verdict = "";
    private string _warnings = "";

    public GameplayViewModel()
    {
        _host.GameplayRecorded += _ => Load();
        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppHost.MonitorStatus) or nameof(AppHost.AutoMeasure))
            {
                Raise(nameof(MonitorStatus));
                Raise(nameof(AutoMeasure));
            }

            if (e.PropertyName == nameof(AppHost.License))
                Load();
        };
        Load();
    }

    public override string Title => "Partidas";

    public ObservableCollection<GameplayItem> Sessions { get; } = [];
    public ObservableCollection<PivotItem> Pivots { get; } = [];
    public ObservableCollection<string> Games { get; } = [];
    public ObservableCollection<MetricItem> Metrics { get; } = [];

    public string MonitorStatus => _host.MonitorStatus;

    public string MeasuredGames => "Jogos reconhecidos: " + string.Join(", ", _host.Ctx.GameProfiles.Select(p => p.Name))
        + ". A medição usa o registro de quadros do próprio Windows, sem mexer no jogo: não conflita com anti-cheat.";

    public bool AutoMeasure
    {
        get => _host.AutoMeasure;
        set => _host.SetAutoMeasure(value);
    }

    public bool StartWithWindows
    {
        get => Autostart.Enabled;
        set
        {
            Autostart.Set(value);
            Raise();
        }
    }

    public bool CanCompare => _host.Allows(Feature.Benchmark);
    public string PlanNote => $"O antes e depois das partidas faz parte do plano {PlanFeatures.RequiredPlan(Feature.Benchmark)}. As partidas continuam sendo medidas e listadas.";

    public PivotItem? SelectedPivot
    {
        get => _pivot;
        set
        {
            if (Set(ref _pivot, value))
                Compare();
        }
    }

    public string? SelectedGame
    {
        get => _game;
        set
        {
            if (Set(ref _game, value))
                Compare();
        }
    }

    public string CompareStatus
    {
        get => _compareStatus;
        private set => Set(ref _compareStatus, value);
    }

    public string Verdict
    {
        get => _verdict;
        private set => Set(ref _verdict, value);
    }

    public string Warnings
    {
        get => _warnings;
        private set => Set(ref _warnings, value);
    }

    public override void OnShown()
    {
        Load();
        Raise(nameof(StartWithWindows));
    }

    private void Load()
    {
        var sessions = _host.Ctx.Gameplay.All();
        Sessions.Clear();
        foreach (var s in sessions)
            Sessions.Add(new GameplayItem(s));

        var game = _game;
        Games.Clear();
        foreach (var name in sessions.Select(s => s.GameName).Distinct())
            Games.Add(name);
        _game = game is not null && Games.Contains(game) ? game : Games.FirstOrDefault();
        Raise(nameof(SelectedGame));

        var pivotId = _pivot?.Session.Id;
        Pivots.Clear();
        foreach (var p in GameplayComparer.Pivots(_host.Ctx.Store.All()))
            Pivots.Add(new PivotItem(p));
        _pivot = Pivots.FirstOrDefault(p => p.Session.Id == pivotId) ?? Pivots.FirstOrDefault();
        Raise(nameof(SelectedPivot));
        Raise(nameof(CanCompare));
        Compare();
    }

    private void Compare()
    {
        Metrics.Clear();
        Verdict = "";
        Warnings = "";
        var sessions = _host.Ctx.Gameplay.All();
        if (sessions.Count == 0)
        {
            CompareStatus = "Nenhuma partida medida ainda. Deixe o FPSX aberto (pode ser na bandeja) e jogue: a medição começa sozinha quando o jogo abre.";
            return;
        }

        if (_pivot is null)
        {
            CompareStatus = "Nenhuma otimização aplicada ainda. Jogue 2 partidas, aplique as otimizações e jogue mais 2: o antes e depois aparece aqui.";
            return;
        }

        if (!CanCompare)
        {
            CompareStatus = PlanNote;
            return;
        }

        var gameId = sessions.FirstOrDefault(s => s.GameName == _game)?.GameId ?? sessions[0].GameId;
        var cmp = GameplayComparer.Compare(sessions, gameId, _game ?? sessions[0].GameName, _pivot.Session.StartedAt, _pivot.Label);
        CompareStatus = cmp.Status;
        if (cmp.Result is not { } r)
            return;

        var culture = CultureInfo.GetCultureInfo("pt-BR");
        foreach (var m in r.Metrics)
        {
            var tone = m.Improved ? (Brush)App.Current.FindResource("Accent") : m.Worsened ? (Brush)App.Current.FindResource("Danger") : (Brush)App.Current.FindResource("Muted");
            var delta = m.Significant
                ? $"{(m.DeltaPercent >= 0 ? "+" : "")}{m.DeltaPercent.ToString("0.0", culture)}%"
                : "sem diferença real";
            Metrics.Add(new MetricItem(m.Metric, m.Before.ToString("0.0", culture), m.After.ToString("0.0", culture), delta, tone));
        }

        Verdict = r.Verdict;
        Warnings = string.Join("\n", r.Warnings.Append(r.Method));
    }
}
