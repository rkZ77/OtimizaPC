using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

namespace Rkzfps.App;

/// <summary>
/// Barra de título própria nos diálogos, igual à da janela principal: a do
/// Windows é branca e destoava do app. Mantém arrastar pela barra e o X.
/// </summary>
public static class ThemedWindow
{
    public static void Apply(Window window, UIElement content)
    {
        var res = Application.Current.Resources;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 34,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        var close = new Button { Content = "", Style = (Style)res["CloseCaptionButton"], ToolTip = "Fechar" };
        close.Click += (_, _) => window.Close();
        var caption = new DockPanel { Height = 34, LastChildFill = false };
        DockPanel.SetDock(close, Dock.Right);
        caption.Children.Add(close);

        var root = new DockPanel();
        DockPanel.SetDock(caption, Dock.Top);
        root.Children.Add(caption);
        root.Children.Add(content);

        // Sem a moldura do Windows, a borda fina separa o diálogo da tela atrás.
        window.Content = new Border { BorderBrush = (Brush)res["Border"], BorderThickness = new Thickness(1), Child = root };
    }
}
