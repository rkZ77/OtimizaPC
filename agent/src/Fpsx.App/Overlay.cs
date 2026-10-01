using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Fpsx.App;

/// <summary>
/// Painel de FPS por cima do jogo, aberto e fechado pelo Ctrl+Shift+F.
///
/// É uma janela comum do Windows, sempre no topo e transparente para o mouse:
/// nada é injetado no jogo, então não conflita com anti-cheat. O custo é que
/// jogo em tela cheia EXCLUSIVA desenha por cima de tudo e o painel não
/// aparece; em janela sem borda (o padrão da maioria dos jogos hoje) aparece.
/// Os números são os mesmos da medição da partida, lidos a cada segundo/5 s.
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly TextBlock _fps = new() { FontSize = 26, FontWeight = FontWeights.Bold };
    private readonly TextBlock _game = new() { FontSize = 12, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _detail = new() { FontSize = 12, TextWrapping = TextWrapping.NoWrap };
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        Title = "RKZFPS painel";
        Left = SystemParameters.WorkArea.Left + 16;
        Top = SystemParameters.WorkArea.Top + 16;

        _fps.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        _game.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        _detail.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var panel = new StackPanel();
        panel.Children.Add(_game);
        panel.Children.Add(_fps);
        panel.Children.Add(_detail);
        var card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 10), BorderThickness = new Thickness(1), Opacity = 0.92, Child = panel };
        card.SetResourceReference(Border.BackgroundProperty, "Surface");
        card.SetResourceReference(Border.BorderBrushProperty, "Border");
        Content = card;

        AppHost.Current.PropertyChanged += OnHost;
        Closed += (_, _) => AppHost.Current.PropertyChanged -= OnHost;
        Refresh();
    }

    private void OnHost(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppHost.LivePoints) or nameof(AppHost.LiveHealth))
            Refresh();
    }

    private void Refresh()
    {
        var host = AppHost.Current;
        if (host.LivePoints is not { Count: > 0 } pts)
        {
            _game.Text = "RKZFPS";
            _fps.Text = "Sem jogo";
            _detail.Text = "O painel mostra o FPS quando a medição de uma partida começa.";
            return;
        }

        var now = pts[^1];
        _game.Text = host.LiveGame ?? "";
        _fps.Text = string.Format(Pt, "{0:0} FPS", now.Fps);
        var parts = new List<string> { string.Format(Pt, "pior quadro {0:0}", now.Low) };
        if (host.LiveHealth is { } h)
        {
            if (h.Cpu is { } c)
                parts.Add(h.CpuMaxCore is { } core ? string.Format(Pt, "CPU {0:0}% (núcleo {1:0}%)", c, core) : string.Format(Pt, "CPU {0:0}%", c));
            if (h.Gpu is { } g)
                parts.Add(h.GpuTempC is { } t ? string.Format(Pt, "GPU {0:0}% {1:0} °C", g, t) : string.Format(Pt, "GPU {0:0}%", g));
        }

        _detail.Text = string.Join("  |  ", parts);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Clique atravessa o painel e ele nunca rouba o foco do jogo.
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, style | WsExTransparent | WsExToolWindow | WsExNoActivate | WsExLayered);
    }

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExLayered = 0x80000;
    private const int WsExNoActivate = 0x8000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
}

/// <summary>Atalho global Ctrl+Shift+F: funciona com o jogo na frente, sem o app estar em foco.</summary>
public sealed class OverlayHotkey : IDisposable
{
    private const int HotkeyId = 0x4650; // "FP"
    private const uint ModControl = 0x2, ModShift = 0x4, ModNoRepeat = 0x4000;
    private const uint KeyF = 0x46;
    private const int WmHotkey = 0x0312;

    private readonly HwndSource _source;
    private OverlayWindow? _overlay;

    /// <summary>false = outro programa já usa o Ctrl+Shift+F.</summary>
    public bool Registered { get; }

    public OverlayHotkey(Window owner)
    {
        var hwnd = new WindowInteropHelper(owner).EnsureHandle();
        _source = HwndSource.FromHwnd(hwnd);
        _source.AddHook(Hook);
        Registered = RegisterHotKey(hwnd, HotkeyId, ModControl | ModShift | ModNoRepeat, KeyF);
    }

    public void Toggle()
    {
        if (_overlay is { IsVisible: true })
        {
            _overlay.Close();
            _overlay = null;
            return;
        }

        _overlay = new OverlayWindow();
        _overlay.Show();
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Toggle();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _overlay?.Close();
        _overlay = null;
        UnregisterHotKey(_source.Handle, HotkeyId);
        _source.RemoveHook(Hook);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
