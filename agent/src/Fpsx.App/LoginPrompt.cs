using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Fpsx.Client;

namespace Fpsx.App;

/// <summary>
/// Primeira tela de quem acabou de instalar: entrar na conta ativa o PC no
/// plano (e o teste grátis do Pro). Quem não quer conta segue no Free, que
/// mostra o diagnóstico inteiro; a pergunta não volta a cada abertura.
/// </summary>
public static class LoginPrompt
{
    public static void ShowIfNeeded()
    {
        var host = AppHost.Current;
        host.RefreshLicense();
        if (host.License.LoggedIn || host.Ctx.Settings.LoginPromptDone)
            return;
        Show();
        host.Ctx.Storage.SaveSettings(host.Ctx.Settings with { LoginPromptDone = true });
    }

    private static void Show()
    {
        var res = Application.Current.Resources;
        var window = new Window
        {
            Title = "Entrar no FPSX",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)res["Surface"],
        };

        var email = new TextBox { Margin = new Thickness(0, 0, 0, 12) };
        var password = new PasswordBox { Margin = new Thickness(0, 0, 0, 16) };
        var error = new TextBlock { Foreground = (Brush)res["Danger"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed };
        var enter = new Button { Content = "Entrar e ativar este PC", Style = (Style)res["Primary"], IsDefault = true };
        var free = new Button { Content = "Continuar no plano Free", Style = (Style)res["Ghost"], HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var signup = new Button { Content = "Criar conta grátis", Style = (Style)res["Ghost"], Margin = new Thickness(0, 0, 8, 0) };
        var forgot = new Button { Content = "Esqueci minha senha", Style = (Style)res["Ghost"] };
        links.Children.Add(signup);
        links.Children.Add(forgot);

        var site = AppHost.Current.SiteUrl;
        signup.Click += (_, _) => AppHost.OpenUrl(site + "/cadastro");
        forgot.Click += (_, _) => AppHost.OpenUrl(site + "/esqueci-senha" + (email.Text.Contains('@') ? "?email=" + Uri.EscapeDataString(email.Text.Trim()) : ""));
        free.Click += (_, _) => window.Close();
        enter.Click += async (_, _) =>
        {
            error.Visibility = Visibility.Collapsed;
            enter.IsEnabled = false;
            enter.Content = "Entrando...";
            try
            {
                var host = AppHost.Current;
                await host.Ctx.LoginAsync(email.Text.Trim(), password.Password, host.WindowsBuild);
                host.RefreshLicense();
                window.Close();
                Dialogs.Info("Conectado", $"Este PC foi ativado na sua conta. Plano: {host.PlanLabel}.");
            }
            catch (ApiException ex) when (ex.Detail is { } d && d.TryGetProperty("devices", out _))
            {
                error.Text = "Sua conta já está ativa em outro PC. Cada assinatura vale para 1 PC: libere o outro em Minha conta, no site, e entre de novo.";
                error.Visibility = Visibility.Visible;
            }
            catch (ApiException ex)
            {
                error.Text = ex.Message;
                error.Visibility = Visibility.Visible;
            }
            catch (System.Net.Http.HttpRequestException)
            {
                error.Text = "Sem conexão com o servidor do FPSX. Confira a internet e tente de novo.";
                error.Visibility = Visibility.Visible;
            }
            finally
            {
                enter.IsEnabled = true;
                enter.Content = "Entrar e ativar este PC";
            }
        };

        var root = new StackPanel { Margin = new Thickness(28) };
        root.Children.Add(new TextBlock { Text = "Entre na sua conta", Style = (Style)res["H2"] });
        root.Children.Add(new TextBlock
        {
            Text = "Use o mesmo e-mail do site. Conta nova ganha dias de teste do plano Pro, sem cartão.",
            Style = (Style)res["MutedText"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 18),
        });
        root.Children.Add(new TextBlock { Text = "E-mail", FontSize = 13, Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(email);
        root.Children.Add(new TextBlock { Text = "Senha", FontSize = 13, Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(password);
        root.Children.Add(error);
        root.Children.Add(enter);
        root.Children.Add(links);
        root.Children.Add(free);
        root.Children.Add(new TextBlock
        {
            Text = "No Free você vê o diagnóstico completo e o que cada otimização resolveria. Para aplicar, entre com um plano.",
            Style = (Style)res["MutedText"], FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
        });
        window.Content = root;
        window.Loaded += (_, _) => email.Focus();
        window.ShowDialog();
    }
}
