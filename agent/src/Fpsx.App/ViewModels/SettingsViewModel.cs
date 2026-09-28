using System.Diagnostics;
using System.Windows.Input;
using Fpsx.Client;
using Fpsx.Core.Catalog;

namespace Fpsx.App.ViewModels;

public sealed class SettingsViewModel : PageViewModel
{
    private readonly AppHost _host = AppHost.Current;
    private ProfileDefinition? _profile;
    private bool _telemetry;

    public SettingsViewModel()
    {
        var s = _host.Ctx.Settings;
        _profile = Profiles.FirstOrDefault(p => p.Id == s.Profile) ?? Profiles.FirstOrDefault();
        _telemetry = s.TelemetryConsent == true;
        OpenDataCommand = new RelayCommand(() => Process.Start(new ProcessStartInfo(_host.Ctx.DataDir) { UseShellExecute = true }));
        PrivacyCommand = new RelayCommand(() => AppHost.OpenUrl(_host.SiteUrl + "/privacidade"));
    }

    public override string Title => "Configurações";

    public override string Icon => "\uE713";

    public ICommand OpenDataCommand { get; }
    public ICommand PrivacyCommand { get; }
    public IReadOnlyList<ProfileDefinition> Profiles => _host.Ctx.Catalog.Profiles;
    public string Version => "FPSX " + AgentContext.Version;

    public ProfileDefinition? Profile
    {
        get => _profile;
        set
        {
            if (Set(ref _profile, value) && value is not null)
                Save(s => s with { Profile = value.Id });
        }
    }

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
                Save(s => s with { TelemetryConsent = value, TelemetryConsentVersion = Fpsx.Client.ClientSettings.CurrentConsentVersion });
                if (!value)
                    _host.Ctx.Storage.ClearQueue();
            }
        }
    }

    private void Save(Func<ClientSettings, ClientSettings> change) => _host.Ctx.Storage.SaveSettings(change(_host.Ctx.Settings));
}
