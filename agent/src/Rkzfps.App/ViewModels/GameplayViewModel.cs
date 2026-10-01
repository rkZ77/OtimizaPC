using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using Rkzfps.Core.Benchmark;
using Rkzfps.Core.Engine;

namespace Rkzfps.App.ViewModels;

public sealed record GameplayItem(GameplaySession Session)
{
    public bool HasChart => Session.Timeline.Count > 1;

    public System.Windows.Media.ImageSource? Icon { get; } = GameIcons.Get(Session.GameId);

    public string Initials => GameIcons.Initials(Session.GameName);

    public string Title => $"{Session.StartedAt.ToLocalTime():dd/MM HH:mm}  {Session.GameName}";

    public string Stats => string.Format(CultureInfo.GetCultureInfo("pt-BR"),
        "FPS médio {0:0}, 1% low {1:0}, 0.1% low {2:0}, frametime médio {3:0.0} ms, {4:0} min medidos",
        Session.Stats.AvgFps, Session.Stats.Low1Fps, Session.Stats.Low01Fps, Session.Stats.AvgFrametimeMs, Session.MeasuredSeconds / 60);

    /// <summary>Calculado na hora (não salvo): regra melhorada vale também para partida antiga.</summary>
    public LimitDiagnosis Diagnosis { get; } = LimitAnalyzer.Diagnose(Session);

    public SessionHealth Health { get; } = SessionHealth.From(Session);

    /// <summary>Uso, temperatura e tela da partida. Só o que foi lido; o que faltou não aparece como zero.</summary>
    public string Details
    {
        get
        {
            var pt = CultureInfo.GetCultureInfo("pt-BR");
            var h = Health;
            var parts = new List<string>();
            var cpu = h.CpuAvg ?? Session.AvgCpuPercent;
            if (cpu is { } c)
                parts.Add(h.CpuMaxCoreAvg is { } core ? string.Format(pt, "CPU {0:0}% (núcleo mais usado {1:0}%)", c, core) : string.Format(pt, "CPU {0:0}%", c));
            var gpu = h.GpuAvg ?? Session.AvgGpuPercent;
            if (gpu is { } g)
                parts.Add(h.GpuTempAvg is { } t ? string.Format(pt, "GPU {0:0}% a {1:0} °C", g, t) : string.Format(pt, "GPU {0:0}%", g));
            if (h.RamAvg is { } r)
                parts.Add(string.Format(pt, "RAM {0:0}%", r));
            if (Session.ScreenWidth is { } w && Session.ScreenHeight is { } hh)
                parts.Add($"{w}x{hh}" + (Session.DisplayHz is { } hz ? $" a {hz} Hz" : ""));
            else if (Session.DisplayHz is { } hz2)
                parts.Add($"monitor a {hz2} Hz");
            if (Session.Gpu is { UsedGpu: { } used } sel && sel.Status != GpuSelectionStatus.Undetermined)
                parts.Add(sel.Status == GpuSelectionStatus.Wrong ? $"rodou na {used} (GPU diferente da de alto desempenho)" : $"rodou na {used}");
            return string.Join(", ", parts);
        }
    }
}

/// <param name="Tag">Medido, Calculado ou Não disponível: a origem de cada número fica visível.</param>
public sealed record EvidenceItem(string Tag, string Text, Brush Tone);

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

    // O seletor do tema mostra o item escolhido pelo texto do item: sem isto
    // aparecia o nome interno do registro no lugar da otimização.
    public override string ToString() => Label;
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

            // Scan: também o aviso de troca de tema (cores do diagnóstico montadas em código).
            if (e.PropertyName is nameof(AppHost.License) or nameof(AppHost.Scan))
                Load();

            if (e.PropertyName == nameof(AppHost.LiveHealth))
                Raise(nameof(LiveHealth));

            if (e.PropertyName == nameof(AppHost.LivePoints))
            {
                Raise(nameof(LivePoints));
                Raise(nameof(IsLive));
                Raise(nameof(LiveTitle));
                Raise(nameof(LiveSummary));
            }
        };
        ShareCommand = new AsyncCommand(Share, () => _selected?.HasChart == true);
        GpuFixCommand = new RelayCommand(_ => ShowGpuFix());
        SaveGoalCommand = new RelayCommand(_ => SaveGoal());
        ExportCommand = new RelayCommand(_ => Export(), _ => Sessions.Count > 0);
        SelectSessionCommand = new RelayCommand(p =>
        {
            if (p is GameplayItem item)
                SelectedSession = item;
        });
        Load();
    }

    public System.Windows.Input.ICommand SelectSessionCommand { get; }

    /// <summary>Imagem da partida para postar no grupo (salva e copiada).</summary>
    public System.Windows.Input.ICommand ShareCommand { get; }

    private async Task Share()
    {
        if (_selected is not { HasChart: true } sel)
            return;
        try
        {
            // Partida de antes da 0.4.2 não guardava o hardware: é o mesmo PC, vale o de agora.
            var session = sel.Session.Hardware is null && _host.Scan is { } scan
                ? sel.Session with { Hardware = HardwareSummary.From(scan.Snapshot) }
                : sel.Session;
            // Espera pouco pelo código: sem internet a imagem sai com o site, sem travar o botão.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var code = await _host.Ctx.ReferralCodeAsync(cts.Token);
            var link = code is null ? null : $"{new Uri(_host.SiteUrl).Host}/r/{code}";
            var path = ShareCard.Export(session, ChartDrops ?? [], ChartCauses, link);
            Dialogs.Info("Imagem pronta", $"A imagem da partida foi copiada: é só colar (Ctrl+V) no Discord ou no WhatsApp. Ela também ficou salva em {path}.");
        }
        catch (System.IO.IOException ex)
        {
            Dialogs.Info("Não deu para salvar", "A pasta de imagens não aceitou o arquivo: " + ex.Message);
        }
    }

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
            var text = string.Format(Pt, "{0:0} FPS agora. Nos últimos {1}: menor média {2:0}, maior {3:0}, pior quadro {4:0} FPS.",
                now.Fps, pts.Count >= 60 ? $"{pts.Count / 60} min" : $"{pts.Count} s", pts.Min(p => p.Fps), pts.Max(p => p.Fps), pts.Min(p => p.Low));
            // Depois de 1 minuto de jogo, avisa se o FPS está bem abaixo da taxa do monitor.
            var hz = _host.Scan?.Snapshot.Displays.FirstOrDefault(d => d.IsPrimary)?.CurrentHz;
            if (pts.Count >= 60 && hz is > 0 && pts.TakeLast(60).Average(p => p.Fps) < hz * DisplayAdvice.AlertBelow)
                text += string.Format(Pt, " Abaixo dos {0} Hz do monitor: o resumo com o que fazer aparece quando a partida terminar.", hz);
            return text;
        }
    }

    /// <summary>
    /// Leitura do PC agora, durante a partida. O que o Windows não deixou ler
    /// aparece como "não disponível", nunca como zero.
    /// </summary>
    public string LiveHealth
    {
        get
        {
            if (_host.LiveHealth is not { } s)
                return "";
            static string V(double? v, string fmt) => v is { } x ? x.ToString(fmt, Pt) : "não disponível";
            return $"CPU {V(s.Cpu, "0")}%, núcleo mais usado {V(s.CpuMaxCore, "0")}%, clock {V(s.CpuClockMhz, "0")} MHz. " +
                   $"GPU {V(s.Gpu, "0")}%, temperatura {V(s.GpuTempC, "0")} °C, clock {V(s.GpuClockPercent, "0")}% do máximo, VRAM {V(s.VramUsedMb / 1024, "0.0")} GB. " +
                   $"RAM {V(s.RamPercent, "0")}%. Temperatura da placa-mãe {V(s.CpuTempC, "0")} °C.";
        }
    }

    // ---- diagnóstico da partida escolhida ----

    public ObservableCollection<EvidenceItem> Evidence { get; } = [];
    public string DiagTitle => _selected?.Diagnosis.Title ?? "";
    public string DiagWhy => _selected?.Diagnosis.Why ?? "";
    public string DiagAdvice => _selected?.Diagnosis.Advice ?? "";

    public Brush DiagBrush => (Brush)App.Current.FindResource(_selected?.Diagnosis.Kind switch
    {
        LimitKind.Balanced => "Accent",
        LimitKind.Insufficient or null => "Muted",
        _ => "Warn",
    });

    /// <summary>Opções gráficas lidas do jogo quando a partida terminou.</summary>
    public string SettingsText => _selected?.Session.GameSettings is { Count: > 0 } gs
        ? string.Join(", ", gs.Select(kv => $"{kv.Key} = {kv.Value}"))
        : _selected?.Session.Detected == true
            ? "Jogo sem perfil no RKZFPS: as opções gráficas dele não são lidas."
            : _selected?.Session.Gpu is null
                ? "Esta partida foi medida antes de o RKZFPS guardar a configuração do jogo."
                : "Configuração do jogo não lida nesta partida (arquivo ausente ou em uso).";

    // ---- placa de vídeo usada pelo jogo ----

    /// <summary>
    /// Partida medida antes desta leitura não tem a placa guardada: o bloco
    /// aparece com "não foi possível determinar" e o motivo, sem deduzir a
    /// placa pelo hardware de hoje (a peça pode ter mudado desde a partida).
    /// </summary>
    private GpuSelection? Gpu => _selected is null ? null : _selected.Session.Gpu ?? new GpuSelection
    {
        Status = GpuSelectionStatus.Undetermined,
        Text = "Esta partida foi medida antes de o RKZFPS registrar a placa de vídeo usada pelo jogo. As próximas partidas já mostram essa informação.",
    };

    public bool HasGpu => Gpu is not null;
    public string GpuUsed => Gpu?.UsedGpu ?? "Não foi possível determinar";
    public string GpuBest => Gpu?.HighPerformanceGpu ?? "Não foi possível determinar";
    public string GpuStatus => Gpu is { } g ? GpuSelector.Label(g.Status) : "";
    public string GpuText => Gpu?.Text ?? "";

    /// <summary>Ícone do Segoe MDL2 (sem emoji): certo, alerta, informação ou dúvida.</summary>
    public string GpuGlyph => Gpu?.Status switch
    {
        GpuSelectionStatus.Correct => "",
        GpuSelectionStatus.Wrong => "",
        GpuSelectionStatus.Undetermined => "",
        _ => "",
    };

    public Brush GpuBrush => (Brush)App.Current.FindResource(Gpu?.Status switch
    {
        GpuSelectionStatus.Correct => "Accent",
        GpuSelectionStatus.Wrong => "Warn",
        _ => "Muted",
    });

    public bool GpuFixable => Gpu?.Status is GpuSelectionStatus.Wrong or GpuSelectionStatus.OtherDedicated;

    public System.Windows.Input.ICommand GpuFixCommand { get; }

    /// <summary>
    /// Explica a correção e leva a quem faz. Nada é alterado daqui: a mudança
    /// sai pela otimização "Rodar o jogo na placa dedicada" (com confirmação,
    /// histórico e desfazer) ou pela pessoa, na tela de Gráficos do Windows.
    /// </summary>
    private void ShowGpuFix()
    {
        if (Gpu is not { } g)
            return;
        var text =
            $"{g.Text}\n\n" +
            "Como corrigir:\n" +
            $"1. Configurações do Windows > Sistema > Tela > Elementos gráficos. Escolha o jogo (ou adicione o .exe dele), clique em Opções e marque Alto desempenho ({g.HighPerformanceGpu}).\n" +
            "2. No PC de mesa, confira se o cabo do monitor está na saída da placa de vídeo e não na da placa-mãe: ligado na placa-mãe, o jogo pode rodar no vídeo integrado.\n" +
            "3. Em notebook, jogue com o carregador ligado: na bateria, alguns modelos seguram a placa dedicada. No painel da NVIDIA ou no AMD Software também dá para escolher a placa por programa.\n\n" +
            "O RKZFPS também faz o passo 1 pela otimização \"Rodar o jogo na placa dedicada\", na tela Otimizações: ela pede confirmação, fica no Histórico e pode ser desfeita. Depois, jogue uma partida para conferir aqui.";
        switch (Dialogs.Show("Placa de vídeo do jogo", text, "Abrir Otimizações", "Abrir Gráficos do Windows", "Fechar"))
        {
            case 0:
                _host.Navigate<OptimizationsViewModel>();
                break;
            case 1:
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:display-advancedgraphics") { UseShellExecute = true })?.Dispose();
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    Dialogs.Info("Não abriu", "Abra Configurações > Sistema > Tela > Elementos gráficos.");
                }

                break;
        }
    }

    private void LoadEvidence()
    {
        Evidence.Clear();
        if (_selected is null)
            return;
        foreach (var e in _selected.Diagnosis.Evidence)
        {
            var (tag, tone) = e.Kind switch
            {
                EvidenceKind.Measured => ("Medido", "Accent"),
                EvidenceKind.Calculated => ("Calculado", "Text"),
                _ => ("Não disponível", "Muted"),
            };
            Evidence.Add(new EvidenceItem(tag, e.Text, (Brush)App.Current.FindResource(tone)));
        }
    }

    // ---- histórico por jogo ----

    public const string AllGames = "Todos os jogos";
    private string _historyGame = AllGames;
    private List<GameplayItem> _all = [];

    public ObservableCollection<string> HistoryGames { get; } = [];

    public string HistoryGame
    {
        get => _historyGame;
        set
        {
            if (Set(ref _historyGame, value ?? AllGames))
                FillHistory();
        }
    }

    /// <summary>Média das partidas do jogo escolhido (só com um jogo escolhido: jogos diferentes não se somam).</summary>
    public string HistorySummary
    {
        get
        {
            var list = Sessions.ToList();
            if (_historyGame == AllGames || list.Count == 0)
                return "";
            var cpu = list.Select(i => i.Health.CpuAvg ?? i.Session.AvgCpuPercent).Where(v => v is not null).Select(v => v!.Value).ToList();
            var gpu = list.Select(i => i.Health.GpuAvg ?? i.Session.AvgGpuPercent).Where(v => v is not null).Select(v => v!.Value).ToList();
            var text = string.Format(Pt, "{0} partida(s) de {1}: FPS médio {2:0}, 1% low {3:0}, 0.1% low {4:0}, frametime {5:0.0} ms",
                list.Count, _historyGame, list.Average(i => i.Session.Stats.AvgFps), list.Average(i => i.Session.Stats.Low1Fps),
                list.Average(i => i.Session.Stats.Low01Fps), list.Average(i => i.Session.Stats.AvgFrametimeMs));
            if (cpu.Count > 0)
                text += string.Format(Pt, ", CPU {0:0}%", cpu.Average());
            if (gpu.Count > 0)
                text += string.Format(Pt, ", GPU {0:0}%", gpu.Average());
            return text + ".";
        }
    }

    // ---- meta de FPS do jogo escolhido ----

    private string _goalText = "";

    private string? HistoryGameId => _historyGame == AllGames ? null : _all.FirstOrDefault(i => i.Session.GameName == _historyGame)?.Session.GameId;

    public bool HasGameSelected => HistoryGameId is not null;

    /// <summary>A meta digitada (texto, para aceitar o campo vazio = sem meta).</summary>
    public string GoalText
    {
        get => _goalText;
        set => Set(ref _goalText, value);
    }

    public string GoalResult
    {
        get
        {
            if (HistoryGameId is not { } id || _host.FpsGoalOf(id) is not { } goal)
                return HasGameSelected ? "Sem meta para este jogo. Digite o FPS que você quer e salve." : "";
            return FpsGoal.Evaluate(_all.Where(i => i.Session.GameId == id).Select(i => i.Session), goal).Text;
        }
    }

    public System.Windows.Input.ICommand SaveGoalCommand { get; }

    private void SaveGoal()
    {
        if (HistoryGameId is not { } id)
            return;
        var text = _goalText.Trim();
        var goal = FpsGoal.Parse(text);
        if (text.Length > 0 && goal is null)
        {
            Dialogs.Info("Meta inválida", $"Digite um número inteiro de FPS entre {FpsGoal.Min} e {FpsGoal.Max}, ou deixe vazio para tirar a meta.");
            return;
        }

        _host.SetFpsGoal(id, goal);
        Raise(nameof(GoalResult));
    }

    private void LoadGoal()
    {
        GoalText = HistoryGameId is { } id && _host.FpsGoalOf(id) is { } g ? g.ToString(Pt) : "";
        Raise(nameof(HasGameSelected));
        Raise(nameof(GoalResult));
    }

    // ---- planilha ----

    public System.Windows.Input.ICommand ExportCommand { get; }

    /// <summary>Exporta as partidas da lista (o filtro de jogo vale) numa planilha que o Excel abre direto.</summary>
    private void Export()
    {
        var list = Sessions.Select(i => i.Session).ToList();
        if (list.Count == 0)
            return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar partidas",
            Filter = "Planilha (*.csv)|*.csv",
            FileName = $"RKZFPS partidas {(_historyGame == AllGames ? "" : _historyGame + " ")}{DateTime.Now:yyyy-MM-dd}.csv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            GameplayCsv.Save(dialog.FileName, list);
            Dialogs.Info("Planilha salva", $"{list.Count} partida(s) exportada(s) para {dialog.FileName}. Abre direto no Excel ou no Google Planilhas.");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Dialogs.Info("Não deu para salvar", "O Windows não aceitou o arquivo nessa pasta (pode estar aberto no Excel): " + ex.Message);
        }
    }

    private void FillHistory()
    {
        Sessions.Clear();
        foreach (var i in _all.Where(i => _historyGame == AllGames || i.Session.GameName == _historyGame))
            Sessions.Add(i);
        LoadGoal();
        Raise(nameof(HistorySummary));
        if (_selected is null || !Sessions.Contains(_selected))
            SelectedSession = Sessions.FirstOrDefault(i => i.HasChart) ?? Sessions.FirstOrDefault();
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
            Raise(nameof(ChartDrops));
            Raise(nameof(ChartCauses));
            Raise(nameof(HzText));
            Raise(nameof(HzBrush));
            Raise(nameof(DiagTitle));
            Raise(nameof(DiagWhy));
            Raise(nameof(DiagAdvice));
            Raise(nameof(DiagBrush));
            Raise(nameof(SettingsText));
            foreach (var n in new[] { nameof(HasGpu), nameof(GpuUsed), nameof(GpuBest), nameof(GpuStatus), nameof(GpuText), nameof(GpuGlyph), nameof(GpuBrush), nameof(GpuFixable) })
                Raise(n);
            LoadEvidence();
            _ = LoadPeersAsync(value);
        }
    }

    private (bool Alert, string Text)? Hz => _selected is { } sel
        ? DisplayAdvice.For(sel.Session.Stats.AvgFps, sel.Session.Stats.Low1Fps, sel.Session.DisplayHz, sel.Session.Timeline)
        : null;

    /// <summary>FPS em relação à taxa do monitor (alerta quando fica bem abaixo).</summary>
    public string HzText => Hz?.Text ?? "";

    public System.Windows.Media.Brush HzBrush => (System.Windows.Media.Brush)App.Current.FindResource(Hz?.Alert == true ? "Warn" : "Accent");

    private string _peerNote = "";

    /// <summary>Como PCs parecidos rodam o mesmo jogo (só com envio de dados ligado e grupo grande o bastante).</summary>
    public string PeerNote
    {
        get => _peerNote;
        private set => Set(ref _peerNote, value);
    }

    private async Task LoadPeersAsync(GameplayItem? item)
    {
        PeerNote = "";
        if (item is null)
            return;
        var peers = await _host.Ctx.PeersAsync(item.Session.GameId);
        // A pessoa pode ter trocado de partida enquanto a resposta chegava.
        if (peers is null || !ReferenceEquals(item, _selected))
            return;
        var who = peers.Scope == "gpu" ? $"PCs com a mesma placa ({peers.Label})" : "PCs do mesmo nível que o seu";
        var s = item.Session.Stats;
        PeerNote = string.Format(Pt, "{0}, {1} no total: FPS médio {2:0} e 1% low {3:0}. Nesta partida você fez {4:0} e {5:0}. Partida real varia com mapa e modo: compare várias.",
            who, peers.Devices, peers.AvgFps, peers.Low1Fps, s.AvgFps, s.Low1Fps);
    }

    /// <summary>Quedas da partida escolhida com o que estava pesando no PC naquele momento.</summary>
    public IReadOnlyList<DropCause>? ChartDrops => _selected is { } sel
        ? StutterExplainer.Explain(sel.Session.Timeline, sel.Session.Load, sel.Session.Stats.AvgFps)
        : null;

    /// <summary>A causa mais comum das quedas, em uma frase. Vazio em partida antiga (sem leitura de uso).</summary>
    public string ChartCauses => _selected is { } sel && sel.Session.Load.Count > 0 && ChartDrops is { Count: > 0 } drops
        ? StutterExplainer.Summary(drops)
        : "";

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
                drops == 0 ? "Nenhuma queda forte: FPS estável na partida." : drops == 1
                    ? "1 queda forte (um quadro 3 vezes mais lento que a média e acima de 33 ms), marcada em vermelho: passe o mouse para ver quando e o que estava pesando no PC."
                    : $"{drops} quedas fortes (um quadro 3 vezes mais lento que a média e acima de 33 ms), marcadas em vermelho: passe o mouse para ver quando e o que estava pesando no PC.");
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
        + ". Outro jogo aberto em tela cheia e usando a placa de vídeo também é medido. A medição usa o registro de quadros e os sensores do próprio Windows, sem mexer no jogo e sem programa extra: não conflita com anti-cheat.";

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
        var selectedId = _selected?.Session.Id;
        _all = sessions.Select(s => new GameplayItem(s)).ToList();
        HistoryGames.Clear();
        HistoryGames.Add(AllGames);
        foreach (var name in sessions.Select(s => s.GameName).Distinct())
            HistoryGames.Add(name);
        if (!HistoryGames.Contains(_historyGame))
            _historyGame = AllGames;
        Raise(nameof(HistoryGame));
        _selected = null;
        FillHistory();
        if (Sessions.FirstOrDefault(i => i.Session.Id == selectedId) is { } again)
            SelectedSession = again;

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
            CompareStatus = "Nenhuma partida medida ainda. Deixe o RKZFPS aberto (pode ser na bandeja) e jogue: a medição começa sozinha quando o jogo abre.";
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

        // Uso de CPU e GPU entra como contexto: não é ganho nem perda por si.
        var muted = (Brush)App.Current.FindResource("Muted");
        foreach (var (name, (b, a)) in new[] { ("Uso de CPU", cmp.Cpu), ("Uso de GPU", cmp.Gpu) })
            if (b is { } vb && a is { } va)
                Metrics.Add(new MetricItem(name, vb.ToString("0", culture) + "%", va.ToString("0", culture) + "%", "contexto, não é ganho", muted));

        Verdict = r.Verdict;
        Warnings = string.Join("\n", r.Warnings.Append(r.Method));
    }
}
