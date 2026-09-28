using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using Fpsx.Core.Benchmark;
using Fpsx.Core.Engine;

namespace Fpsx.App.ViewModels;

public sealed record GameplayItem(GameplaySession Session)
{
    public bool HasChart => Session.Timeline.Count > 1;

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
    private GameplayItem? _selected;

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

            if (e.PropertyName == nameof(AppHost.LivePoints))
            {
                Raise(nameof(LivePoints));
                Raise(nameof(IsLive));
                Raise(nameof(LiveTitle));
                Raise(nameof(LiveSummary));
            }
        };
        SelectSessionCommand = new RelayCommand(p =>
        {
            if (p is GameplayItem item)
                SelectedSession = item;
        });
        Load();
    }

    public System.Windows.Input.ICommand SelectSessionCommand { get; }

    // ---- gráfico ao vivo ----

    public IReadOnlyList<FpsPoint>? LivePoints => _host.LivePoints;
    public bool IsLive => _host.LivePoints is { Count: > 0 };
    public string LiveTitle => $"{_host.LiveGame} agora";

    public string LiveSummary
    {
        get
        {
            if (_host.LivePoints is not { Count: > 0 } pts)
                return "";
            var now = pts[^1];
            return string.Format(Pt, "{0:0} FPS agora. Nos últimos {1}: menor média {2:0}, maior {3:0}, pior quadro {4:0} FPS.",
                now.Fps, pts.Count >= 60 ? $"{pts.Count / 60} min" : $"{pts.Count} s", pts.Min(p => p.Fps), pts.Max(p => p.Fps), pts.Min(p => p.Low));
        }
    }

    // ---- gráfico da partida escolhida ----

    public GameplayItem? SelectedSession
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value))
                return;
            Raise(nameof(ChartPoints));
            Raise(nameof(ChartHz));
            Raise(nameof(ChartTitle));
            Raise(nameof(ChartNote));
        }
    }

    public IReadOnlyList<FpsPoint>? ChartPoints => _selected?.Session.Timeline;
    public int? ChartHz => _selected?.Session.DisplayHz;
    public string ChartTitle => _selected is null ? "" : _selected.Title;

    public string ChartNote
    {
        get
        {
            if (_selected is null)
                return "";
            var s = _selected.Session;
            if (s.Timeline.Count < 2)
                return "Partida medida antes do gráfico existir: só os números ficaram guardados.";
            var drops = GameplayAnalyzer.Drops(s.Timeline, s.Stats.AvgFps);
            return string.Format(Pt, "Média {0:0} FPS, 1% low {1:0}. {2}", s.Stats.AvgFps, s.Stats.Low1Fps,
                drops == 0 ? "Nenhuma queda forte: FPS estável na partida." : $"{drops} {(drops == 1 ? "queda forte" : "quedas fortes")} (um quadro 3 vezes mais lento que a média e acima de 33 ms): passe o mouse no gráfico para ver quando.");
        }
    }

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public override string Title => "Partidas e FPS";

    public override string Icon => "\uEC4A";

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
    public string PlanNote => $"O antes e depois das partidas faz parte do plano {Plans.Label(PlanFeatures.RequiredPlan(Feature.Benchmark))}. As partidas continuam sendo medidas e listadas.";

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
        SelectedSession = Sessions.FirstOrDefault(i => i.Session.Id == _selected?.Session.Id)
                          ?? Sessions.FirstOrDefault(i => i.HasChart) ?? Sessions.FirstOrDefault();

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
        var cmp = GameplayComparer.Compare(sessions, gameId, _game ?? sessions[0].GameName, _pivot.Session.StartedAt, _pivot.Label,
            GameplayComparer.ShaderClears(_host.Ctx.Store.All()));
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
