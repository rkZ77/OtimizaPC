using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Fpsx.App;

/// <summary>
/// Ícone na bandeja: com a medição automática ligada, fechar a janela deixa
/// o RKZFPS na bandeja medindo as partidas. "Sair" encerra de verdade.
/// </summary>
public sealed class Tray : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _measure;
    private bool _hintShown;

    public Tray(Action open, Action exit)
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/fpsx.ico"))!.Stream;
        _measure = new Forms.ToolStripMenuItem("Medir FPS das partidas") { CheckOnClick = true, Checked = AppHost.Current.Ctx.Settings.AutoMeasure };
        _measure.CheckedChanged += (_, _) => AppHost.Current.SetAutoMeasure(_measure.Checked);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir RKZFPS", null, (_, _) => open());
        menu.Items.Add(_measure);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => exit());
        _icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream),
            Text = "RKZFPS",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => open();
        _icon.BalloonTipClicked += (_, _) => open();
        AppHost.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppHost.AutoMeasure))
                _measure.Checked = AppHost.Current.AutoMeasure;
            // FPS ao vivo no texto do ícone: passar o mouse perto do relógio
            // mostra o número sem sair do jogo. Limite de 63 caracteres do Windows.
            if (e.PropertyName == nameof(AppHost.LiveFps))
            {
                var text = AppHost.Current.LiveFps is { } fps ? "RKZFPS. " + fps : "RKZFPS";
                _icon.Text = text.Length > 63 ? text[..63] : text;
            }
        };
    }

    public void Notify(string title, string text) => _icon.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.Info);

    /// <summary>Na primeira vez que a janela vai para a bandeja, explica onde o app foi parar.</summary>
    public void HintOnce()
    {
        if (_hintShown)
            return;
        _hintShown = true;
        Notify("O RKZFPS continua aberto", "Ele fica aqui na bandeja medindo o FPS das suas partidas. Para fechar de vez, clique com o botão direito e escolha Sair.");
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}

/// <summary>
/// Iniciar com o Windows pela chave Run do próprio usuário: não precisa de
/// administrador e aparece no Gerenciador de Tarefas, onde a pessoa pode
/// desligar quando quiser.
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string Command => $"\"{Environment.ProcessPath}\" --tray";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Core.Optimizations.StartupOptimization.OwnStartupName) is string;
        }
    }

    public static void Set(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (on)
            key.SetValue(Core.Optimizations.StartupOptimization.OwnStartupName, Command, RegistryValueKind.String);
        else
            key.DeleteValue(Core.Optimizations.StartupOptimization.OwnStartupName, throwOnMissingValue: false);
    }
}
