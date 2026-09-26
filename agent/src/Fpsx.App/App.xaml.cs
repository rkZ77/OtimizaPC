using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Fpsx.App;

public partial class App : Application
{
    private Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Uma instância só: duas aplicando alterações ao mesmo tempo
        // disputariam o mesmo backup.
        _single = new Mutex(true, @"Local\FPSX.App.Single", out var first);
        if (!first)
        {
            Dialogs.Info("FPSX já está aberto", "O FPSX já está rodando. Procure a janela na barra de tarefas.");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandled;
        // Até a janela principal existir, fechar um diálogo não encerra o app
        // (o WPF trataria o primeiro diálogo como janela principal).
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        try
        {
            _ = AppHost.Current; // carrega catálogo e perfis antes da janela
        }
        catch (Exception ex)
        {
            Dialogs.Log(ex);
            Dialogs.Info("Instalação incompleta", "O catálogo de otimizações do FPSX não foi encontrado ou está corrompido. Reinstale o FPSX.");
            Shutdown();
            return;
        }

        AskTelemetryOnce();
        var main = new MainWindow();
        MainWindow = main;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        main.Show();
    }

    /// <summary>Consentimento explícito, perguntado uma vez (seção 40). O padrão é NÃO enviar.</summary>
    private static void AskTelemetryOnce()
    {
        var ctx = AppHost.Current.Ctx;
        if (ctx.Settings.TelemetryConsent is not null)
            return;
        var yes = Dialogs.Show("Ajude a melhorar o FPSX",
            "Podemos enviar dados anônimos de uso? Somente: quais otimizações foram aplicadas, se funcionaram, resultados de benchmark, versão do app e do Windows.\n\n" +
            "Nunca enviamos arquivos, nomes de programas, caminhos de pasta ou qualquer conteúdo pessoal. Você pode mudar isso a qualquer momento em Configurações.",
            "Permitir", "Não permitir") == 0;
        ctx.Storage.SaveSettings(ctx.Settings with { TelemetryConsent = yes });
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Dialogs.Error(e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }
}
