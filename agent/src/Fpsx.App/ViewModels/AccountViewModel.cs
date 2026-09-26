using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using Fpsx.Client;
using Fpsx.Core.Engine;

namespace Fpsx.App.ViewModels;

public sealed record FeatureItem(string Label, bool Included, string Plan);

/// <summary>Conta: login, ativação deste PC, plano e o que cada plano libera.</summary>
public sealed class AccountViewModel : PageViewModel
{
    private readonly AppHost _host = AppHost.Current;
    private string _email = "";

    public AccountViewModel()
    {
        // A senha vem do PasswordBox (que não aceita binding, de propósito) e
        // não fica guardada em propriedade nenhuma.
        LoginCommand = new AsyncCommand(p => Busy(() => Login((p as System.Windows.Controls.PasswordBox)?.Password ?? "")), _ => !IsBusy && Email.Contains('@'));
        LogoutCommand = new AsyncCommand(() => Busy(Logout), () => !IsBusy && _host.License.LoggedIn);
        SyncCommand = new AsyncCommand(() => Busy(_host.SyncAsync), () => !IsBusy && _host.License.LoggedIn);
        PlansCommand = new RelayCommand(() => AppHost.OpenUrl(_host.SiteUrl + "/planos"));
        SiteCommand = new RelayCommand(() => AppHost.OpenUrl(_host.SiteUrl + "/conta"));
        SignupCommand = new RelayCommand(() => AppHost.OpenUrl(_host.SiteUrl + "/cadastro"));
        // A troca de senha é no site: o código chega por e-mail e a tela de lá
        // já sabe pedir, validar e avisar. Leva o e-mail digitado junto.
        ForgotCommand = new RelayCommand(() => AppHost.OpenUrl(_host.SiteUrl + "/esqueci-senha"
            + (Email.Contains('@') ? "?email=" + Uri.EscapeDataString(Email.Trim()) : "")));
        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppHost.License))
                Load();
        };
        Load();
    }

    public override string Title => "Conta";

    public override string Icon => "\uE77B";

    public ICommand LoginCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand SyncCommand { get; }
    public ICommand PlansCommand { get; }
    public ICommand SiteCommand { get; }
    public ICommand SignupCommand { get; }
    public ICommand ForgotCommand { get; }

    public string Email
    {
        get => _email;
        set => Set(ref _email, value);
    }

    public LicenseState License => _host.License;
    public bool LoggedIn => License.LoggedIn;
    public string PlanLabel => _host.PlanLabel;
    public string Expires => License.ExpiresAt is { } e ? $"Válido até {e.ToLocalTime():dd/MM/yyyy}" : "Sem vencimento";
    public string Offline => License.ValidUntil is { } v ? $"Funciona offline até {v.ToLocalTime():dd/MM/yyyy HH:mm}" : "";
    public ObservableCollection<FeatureItem> Features { get; } = [];

    private void Load()
    {
        Features.Clear();
        foreach (var f in Enum.GetValues<Feature>())
        {
            var plan = PlanFeatures.RequiredPlan(f);
            Features.Add(new FeatureItem(PlanFeatures.Label(f), _host.Allows(f), char.ToUpperInvariant(plan[0]) + plan[1..]));
        }
        foreach (var n in new[] { nameof(License), nameof(LoggedIn), nameof(PlanLabel), nameof(Expires), nameof(Offline) })
            Raise(n);
    }

    private async Task Login(string password)
    {
        try
        {
            await _host.Ctx.LoginAsync(Email.Trim(), password, _host.WindowsBuild);
            _host.RefreshLicense();
            Dialogs.Info("Conectado", $"Este PC foi ativado na sua conta. Plano: {_host.PlanLabel}.");
        }
        catch (ApiException ex) when (ex.Detail is { } d && d.TryGetProperty("devices", out var devices))
        {
            var list = string.Join("\n", devices.EnumerateArray().Select(dev => "• " + dev.GetProperty("name").GetString()));
            if (Dialogs.Show("Limite de PCs atingido", $"{ex.Message}\n\nPCs ativos na sua conta:\n{list}", "Gerenciar PCs no site", "Fechar") == 0)
                AppHost.OpenUrl(_host.SiteUrl + "/conta");
        }
    }

    private async Task Logout()
    {
        if (!Dialogs.Confirm("Sair da conta", "Este PC vai liberar a vaga na sua conta e voltar ao plano Free. As alterações já feitas continuam e podem ser desfeitas no Histórico.", "Sair"))
            return;
        await _host.Ctx.LogoutAsync();
        _host.RefreshLicense();
    }
}
