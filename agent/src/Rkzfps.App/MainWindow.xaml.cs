using System.Windows;
using System.Windows.Controls;
using Rkzfps.App.ViewModels;

namespace Rkzfps.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += async (_, _) =>
        {
            await _vm.StartAsync();
            // Primeira coisa ao abrir: diagnóstico (só leitura). O fluxo da seção 46
            // é detectar e analisar antes de oferecer qualquer alteração.
            if (_vm.AllPages[0] is DashboardViewModel dash && dash.ScanCommand.CanExecute(null))
                dash.ScanCommand.Execute(null);
            // Instalação nova: perguntas rápidas enquanto o scan acima roda. O
            // guia antigo fica em "Como usar o RKZFPS", para não somar duas
            // janelas antes de usar. Quem já usava o app não vê nenhuma das duas.
            var settings = AppHost.Current.Ctx.Settings;
            if (settings.NeedsOnboarding)
                OnboardingWindow.Show();
            else if (!settings.TutorialDone)
                Tutorial.Show();
            else
                WhatsNew.ShowIfUpdated();
            WhatsNew.MarkSeen();
        };
        StateChanged += (_, _) => AjustarMaximizado();
        MostrarTema();
        ThemeManager.Changed += MostrarTema;
        Closed += (_, _) => ThemeManager.Changed -= MostrarTema;
    }

    private void Theme_Click(object sender, RoutedEventArgs e) => ThemeManager.Toggle();

    /// <summary>O ícone e a dica mostram para onde o botão leva, como no site.</summary>
    private void MostrarTema()
    {
        ThemeButton.Tag = ThemeManager.IsLight ? ThemeManager.Light : ThemeManager.Dark;
        ThemeButton.ToolTip = ThemeManager.IsLight ? "Tema escuro" : "Tema claro";
    }

    // ---- barra de título própria ----

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // Fechar segue o caminho de sempre: com a medição ligada, vai para a bandeja.
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Maximizada sem a moldura do Windows, a janela passa uns pixels de cada
    /// lado da tela e corta a barra de título. A margem devolve o que sobra.
    /// </summary>
    private void AjustarMaximizado()
    {
        var max = WindowState == WindowState.Maximized;
        Root.Margin = max ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = max ? "" : "";
        MaxButton.ToolTip = max ? "Restaurar" : "Maximizar";
    }
}
