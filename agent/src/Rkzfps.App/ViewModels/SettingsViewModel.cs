using System.Diagnostics;
using System.Windows.Input;
using Rkzfps.Client;
using Rkzfps.Core.Catalog;
using Rkzfps.Core.Engine;

namespace Rkzfps.App.ViewModels;

public sealed record ProfileOption(ProfileChoice Choice, string Label, string Description);

public sealed class SettingsViewModel : PageViewModel
{
    private readonly AppHost _host = AppHost.Current;
    private bool _telemetry;

    public SettingsViewModel()
    {
        var s = _host.Ctx.Settings;
        _telemetry = s.TelemetryConsent == true;
        AskAgainCommand = new RelayCommand(() => OnboardingWindow.Show());
        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppHost.Tuning))
                foreach (var n in new[] { nameof(Choice), nameof(AdvancedProfile), nameof(AdvancedNote), nameof(TuningSummary), nameof(TuningReasons) })
                    Raise(n);
        };
        OpenDataCommand = new RelayCommand(() => Process.Start(new ProcessStartInfo(_host.Ctx.DataDir) { UseShellExecute = true }));
        PrivacyCommand = new RelayCommand(() => AppHost.OpenUrl(_host.SiteUrl + "/privacidade"));
        InstagramCommand = new RelayCommand(() => AppHost.OpenUrl(AppHost.InstagramUrl));
        // O botão da barra de título também troca: o seletor aqui acompanha.
        ThemeManager.Changed += () =>
        {
            Raise(nameof(IsDark));
            Raise(nameof(IsLight));
        };
    }

    private string _overlayStatus = "";

    public bool OverlayHotkey
    {
        get => _host.Ctx.Settings.OverlayHotkey;
        set
        {
            Save(s => s with { OverlayHotkey = value });
            var ok = ((App)System.Windows.Application.Current).ApplyOverlayHotkey();
            OverlayStatus = ok ? "" : "Outro programa já usa o Ctrl+Shift+F. Feche o programa que usa esse atalho e ligue de novo.";
            Raise();
        }
    }

    public string OverlayStatus
    {
        get => _overlayStatus;
        private set => Set(ref _overlayStatus, value);
    }

    public bool IsDark
    {
        get => !ThemeManager.IsLight;
        set
        {
            if (value)
                ThemeManager.Set(ThemeManager.Dark);
        }
    }

    public bool IsLight
    {
        get => ThemeManager.IsLight;
        set
        {
            if (value)
                ThemeManager.Set(ThemeManager.Light);
        }
    }

    public override string Title => "Configurações";

    public override string Icon => "\uE713";

    public ICommand OpenDataCommand { get; }
    public ICommand PrivacyCommand { get; }
    public ICommand InstagramCommand { get; }
    public IReadOnlyList<ProfileDefinition> Profiles => _host.Ctx.Catalog.Profiles;
    public string Version => "RKZFPS " + AgentContext.Version;

    // ---- perfil: os 5 da tela por cima dos 7 internos ----

    public ICommand AskAgainCommand { get; }

    public IReadOnlyList<ProfileOption> Choices { get; } =
        new[] { ProfileChoice.Automatic, ProfileChoice.Performance, ProfileChoice.Balanced, ProfileChoice.Quality, ProfileChoice.Custom }
            .Select(c => new ProfileOption(c, UserPreferences.Label(c), UserPreferences.Description(c)))
            .ToList();

    /// <summary>Escolha atual. Quem nunca respondeu aparece no equivalente do perfil salvo.</summary>
    private ProfileChoice CurrentChoice
    {
        get
        {
            var s = _host.Ctx.Settings;
            return s.Preferences?.Choice ?? UserPreferences.FromLegacy(s.Profile);
        }
    }

    /// <summary>null quando o perfil em uso é um dos internos da área avançada.</summary>
    public ProfileOption? Choice
    {
        get => Choices.FirstOrDefault(c => c.Choice == CurrentChoice);
        set
        {
            if (value is null || value.Choice == CurrentChoice)
                return;
            // Mantém as respostas das perguntas: só o perfil da tela muda.
            _host.SetPreferences((_host.Ctx.Settings.Preferences ?? new UserPreferences()) with { Choice = value.Choice });
        }
    }

    /// <summary>Área avançada: os 7 perfis internos, streaming incluído.</summary>
    public ProfileDefinition? AdvancedProfile
    {
        get => CurrentChoice == ProfileChoice.Advanced ? Profiles.FirstOrDefault(p => p.Id == _host.Ctx.Settings.Profile) : null;
        set
        {
            if (value is null)
                return;
            _host.SetPreferences((_host.Ctx.Settings.Preferences ?? new UserPreferences()) with { Choice = ProfileChoice.Advanced }, value.Id);
        }
    }

    public string AdvancedNote => AdvancedProfile is { } p ? $"Em uso o perfil avançado {p.Name}. Escolha um perfil acima para voltar aos principais." : "";

    public string TuningSummary => _host.Tuning is { Legacy: false } t ? t.Summary : "";

    public string TuningReasons => _host.Tuning is { Legacy: false } t ? string.Join("\n", t.Reasons) : "";

    public bool Advanced
    {
        get => _host.AdvancedMode;
        set
        {
            _host.SetAdvancedMode(value);
            Raise();
        }
    }

    public bool Telemetry
    {
        get => _telemetry;
        set
        {
            if (Set(ref _telemetry, value))
            {
                // Marcar aqui, com o texto atual ao lado, vale como o consentimento atual.
                Save(s => s with { TelemetryConsent = value, TelemetryConsentVersion = Rkzfps.Client.ClientSettings.CurrentConsentVersion });
                if (!value)
                    _host.Ctx.Storage.ClearQueue();
            }
        }
    }

    public bool WatchNotify
    {
        get => _host.Ctx.Settings.WatchNotify;
        set
        {
            Save(s => s with { WatchNotify = value });
            Raise();
        }
    }

    private void Save(Func<ClientSettings, ClientSettings> change) => _host.Ctx.Storage.SaveSettings(change(_host.Ctx.Settings));
}
