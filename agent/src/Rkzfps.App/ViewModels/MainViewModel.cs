using System.Windows.Input;
using Rkzfps.Client;

namespace Rkzfps.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppHost _host = AppHost.Current;
    private PageViewModel _current;

    public MainViewModel()
    {
        AllPages = [new DashboardViewModel(), new OptimizationsViewModel(), new GamesViewModel(), new GameplayViewModel(), new BenchmarkViewModel(), new DriversViewModel(), new HistoryViewModel(), new AccountViewModel(), new UpdatesViewModel(), new SettingsViewModel()];
        _current = AllPages[0];
#if DEBUG
        // Só em desenvolvimento: abrir direto numa tela, para os prints de QA.
        if (int.TryParse(Environment.GetEnvironmentVariable("RKZFPS_START_PAGE"), out var page) && page >= 0 && page < AllPages.Count)
            _current = AllPages[page];
#endif
        RefreshPages();
        _current.IsCurrent = true;
        NavigateCommand = new RelayCommand(p => Current = (PageViewModel)p!);
        TutorialCommand = new RelayCommand(() => Tutorial.Show());
        _host.NavigateRequested += type =>
        {
            if (AllPages.FirstOrDefault(p => p.GetType() == type) is { } target)
            {
                // Atalho para tela do modo avançado liga o avançado: quem
                // clicou quer ver aquilo.
                if (target.AdvancedOnly && !_host.AdvancedMode)
                    _host.SetAdvancedMode(true);
                Current = target;
                Raise(nameof(Current));
            }
        };
        RelaunchCommand = new RelayCommand(() =>
        {
            if (AppHost.RelaunchAsAdmin())
                System.Windows.Application.Current.Shutdown();
        });
        UpdateCommand = new AsyncCommand(RunUpdate, () => UpdateButton == "Atualizar agora");
        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppHost.GamingMode))
            {
                Raise(nameof(GamingLabel));
                Raise(nameof(GamingDot));
            }

            if (e.PropertyName == nameof(AppHost.PlanLabel))
            {
                Raise(nameof(PlanLabel));
                Raise(nameof(PlanHint));
            }

            if (e.PropertyName == nameof(AppHost.AdvancedMode))
                RefreshPages();
            if (e.PropertyName == nameof(AppHost.LiveFps))
                Raise(nameof(LiveFps));
            if (e.PropertyName == nameof(AppHost.Update))
                Raise(nameof(UpdateAvailable));
        };
    }

    public IReadOnlyList<PageViewModel> AllPages { get; }

    /// <summary>Telas do menu no modo atual. O simples esconde as de detalhe técnico.</summary>
    public System.Collections.ObjectModel.ObservableCollection<PageViewModel> Pages { get; } = [];

    public ICommand TutorialCommand { get; }

    public string? LiveFps => _host.LiveFps;

    private void RefreshPages()
    {
        Pages.Clear();
        foreach (var p in AllPages.Where(p => _host.AdvancedMode || !p.AdvancedOnly))
            Pages.Add(p);
        if (!Pages.Contains(_current))
            Current = Pages[0];
    }

    public PageViewModel Current
    {
        get => _current;
        set
        {
            if (Set(ref _current, value))
                value.OnShown();
            foreach (var p in AllPages)
                p.IsCurrent = p == value;
        }
    }

    public ICommand NavigateCommand { get; }
    public ICommand RelaunchCommand { get; }
    public ICommand UpdateCommand { get; }
    public string PlanLabel => _host.PlanLabel;

    public string GamingLabel => _host.GamingMode == Rkzfps.Core.Engine.GamingModeKind.Automatic ? "Modo Gaming: Automático" : "Modo Gaming: Manual";

    public System.Windows.Media.Brush GamingDot => (System.Windows.Media.Brush)App.Current.FindResource(
        _host.GamingMode == Rkzfps.Core.Engine.GamingModeKind.Automatic ? "Accent" : "Muted");

    /// <summary>No Free, lembra que aplicar exige plano; nos pagos fica vazio.</summary>
    public string PlanHint => _host.License.Plan == "free" ? "No Free você vê tudo. Para aplicar, entre com um plano em Conta." : "";
    public bool IsElevated => _host.IsElevated;

    /// <summary>Faixa do topo: versão nova e o que ela traz. null = app em dia.</summary>
    public string? UpdateAvailable => _host.Update is { } u
        ? $"Nova versão {u.Version} disponível. {u.Notes}".Trim()
        : null;

    private string _updateButton = "Atualizar agora";

    public string UpdateButton
    {
        get => _updateButton;
        private set => Set(ref _updateButton, value);
    }

    private async Task RunUpdate()
    {
        try
        {
            UpdateButton = "Baixando...";
            await _host.UpdateNowAsync(new Progress<int>(p => UpdateButton = $"Baixando {p}%"));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Net.Http.HttpRequestException or System.IO.IOException)
        {
            UpdateButton = "Atualizar agora";
            Dialogs.Info("Não foi possível atualizar", ex.Message + "\n\nO RKZFPS continua funcionando na versão atual. Você também pode baixar pelo site.");
        }
    }

    /// <summary>Na abertura: licença local, depois sync e verificação de versão em segundo plano.</summary>
    public async Task StartAsync()
    {
        _host.RefreshLicense();
        try
        {
            await _host.SyncAsync();
        }
        catch (ApiException)
        {
            // Offline: o app funciona com a licença salva até o fim da carência.
        }

        _host.StartUpdateChecks();
    }
}
