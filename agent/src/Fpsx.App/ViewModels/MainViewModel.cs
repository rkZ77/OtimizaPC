using System.Windows.Input;
using Fpsx.Client;

namespace Fpsx.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppHost _host = AppHost.Current;
    private PageViewModel _current;
    private string? _update;

    public MainViewModel()
    {
        Pages = [new DashboardViewModel(), new OptimizationsViewModel(), new GamesViewModel(), new BenchmarkViewModel(), new HistoryViewModel(), new AccountViewModel(), new SettingsViewModel()];
        _current = Pages[0];
#if DEBUG
        // Só em desenvolvimento: abrir direto numa tela, para os prints de QA.
        if (int.TryParse(Environment.GetEnvironmentVariable("FPSX_START_PAGE"), out var page) && page >= 0 && page < Pages.Count)
            _current = Pages[page];
#endif
        NavigateCommand = new RelayCommand(p => Current = (PageViewModel)p!);
        RelaunchCommand = new RelayCommand(() =>
        {
            if (AppHost.RelaunchAsAdmin())
                System.Windows.Application.Current.Shutdown();
        });
        UpdateCommand = new RelayCommand(() => AppHost.OpenUrl(_host.SiteUrl + "/download"));
        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppHost.PlanLabel))
                Raise(nameof(PlanLabel));
        };
    }

    public IReadOnlyList<PageViewModel> Pages { get; }

    public PageViewModel Current
    {
        get => _current;
        set
        {
            if (Set(ref _current, value))
                value.OnShown();
        }
    }

    public ICommand NavigateCommand { get; }
    public ICommand RelaunchCommand { get; }
    public ICommand UpdateCommand { get; }
    public string PlanLabel => _host.PlanLabel;
    public bool IsElevated => _host.IsElevated;

    public string? UpdateAvailable
    {
        get => _update;
        private set => Set(ref _update, value);
    }

    /// <summary>Na abertura: licença local, depois sync e verificação de versão em segundo plano.</summary>
    public async Task StartAsync()
    {
        _host.RefreshLicense();
        try
        {
            await _host.SyncAsync();
            var releases = await _host.Ctx.Api().ReleasesAsync();
            var agent = releases.FirstOrDefault(r => r.Component == "agent");
            if (agent is not null && Version.TryParse(agent.Version, out var latest) && Version.TryParse(AgentContext.Version, out var mine) && latest > mine)
                UpdateAvailable = $"Nova versão {agent.Version} disponível.";
        }
        catch (ApiException)
        {
            // Offline: o app funciona com a licença salva até o fim da carência.
        }
    }
}
