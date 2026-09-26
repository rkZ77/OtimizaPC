using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Fpsx.App;

public partial class App : Application
{
    private const string ShowSignalName = @"Local\FPSX.App.Show";

    private Mutex? _single;
    private EventWaitHandle? _showSignal;
    private Tray? _tray;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Processo elevado (UAC) aberto pelo próprio app para uma tarefa só:
        // usa a mesma pasta de dados de quem pediu e fecha ao terminar.
        var dataDir = Array.IndexOf(e.Args, "--data-dir");
        if (dataDir >= 0 && dataDir + 1 < e.Args.Length)
            Environment.SetEnvironmentVariable("FPSX_DATA_DIR", e.Args[dataDir + 1]);
        if (e.Args.Length >= 2 && e.Args[0] == ElevatedHelper.Flag)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            int code;
            try
            {
                code = ElevatedHelper.Execute(e.Args[1]);
            }
            catch (Exception ex)
            {
                Dialogs.Log(ex);
                Dialogs.Error(ex);
                code = 1;
            }

            Shutdown(code);
            return;
        }

        // Uma instância só: duas aplicando alterações ao mesmo tempo
        // disputariam o mesmo backup, e dois monitores mediriam a mesma partida.
        _single = new Mutex(true, @"Local\FPSX.App.Single", out var first);
        if (!first)
        {
            // Já está aberto (talvez só na bandeja): pede para ele mostrar a janela.
            if (EventWaitHandle.TryOpenExisting(ShowSignalName, out var signal))
                signal.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandled;
        // A janela fecha para a bandeja: quem encerra o app é o "Sair".
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

        var startInTray = e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
        if (!startInTray)
        {
            LoginPrompt.ShowIfNeeded();
            AskTelemetryOnce();
        }

        var main = new MainWindow();
        MainWindow = main;
        main.Closing += (_, args) =>
        {
            // Com a medição ligada, fechar esconde: o monitor segue medindo.
            if (!_exiting && AppHost.Current.AutoMeasure)
            {
                args.Cancel = true;
                main.Hide();
                _tray?.HintOnce();
            }
        };
        main.Closed += (_, _) => ExitApp();

        _tray = new Tray(ShowMain, ExitApp);
        AppHost.Current.GameplayRecorded += s => _tray?.Notify($"{s.GameName}: partida registrada",
            $"FPS médio {s.Stats.AvgFps:0}, 1% low {s.Stats.Low1Fps:0}. Veja o antes e depois em Partidas.");
        AppHost.Current.StartMonitor();

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        new Thread(() =>
        {
            while (_showSignal.WaitOne())
                Dispatcher.BeginInvoke(ShowMain);
        }) { IsBackground = true, Name = "fpsx-show-signal" }.Start();

        if (!startInTray)
            main.Show();
    }

    private void ShowMain()
    {
        if (MainWindow is not { } w)
            return;
        if (!w.IsVisible)
            w.Show();
        if (w.WindowState == WindowState.Minimized)
            w.WindowState = WindowState.Normal;
        w.Activate();
    }

    private void ExitApp()
    {
        if (_exiting)
            return;
        _exiting = true;
        _tray?.Dispose();
        _tray = null;
        AppHost.Current.ShutdownMonitor();
        Shutdown();
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
        _tray?.Dispose();
        _showSignal?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}
