using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Fpsx.App;

/// <summary>
/// Guia de 4 passos da primeira abertura, para quem nunca mexeu em
/// configuração de PC. No fim a pessoa escolhe o modo: simples (o essencial)
/// ou avançado (tudo, item a item). Reabre pelo "Como usar o RKZFPS".
/// </summary>
public static class Tutorial
{
    private sealed record Step(string Glyph, string Title, string Text);

    private static readonly Step[] Steps =
    [
        new("", "1. O RKZFPS analisa o seu PC",
            "Ao abrir, ele confere processador, placa de vídeo, memória, disco, energia, monitor, rede e os seus jogos. " +
            "Essa parte só lê: nada muda no PC. Na tela Início você vê um resumo: se está tudo bem ou o que pode melhorar."),
        new("", "2. Corrige com um clique, e desfaz também",
            "Quando algo pode melhorar, o botão Corrigir agora aplica só o que faz sentido para o seu PC. " +
            "Antes de mudar qualquer coisa, o RKZFPS guarda como estava. Em Histórico e desfazer, tudo volta com um clique."),
        new("", "3. Mede o FPS dos seus jogos sozinho",
            "Deixe o RKZFPS aberto (pode ser só o ícone perto do relógio) e jogue. O FPS aparece ao vivo no topo do app, " +
            "e cada partida fica salva em Partidas e FPS. Depois de otimizar, o app mostra se o FPS mudou de verdade."),
        new("", "4. Como você prefere usar?",
            "Modo simples: só o essencial, com botões grandes. Modo avançado: mostra também cada otimização, o benchmark " +
            "e os detalhes técnicos. Dá para trocar quando quiser em Configurações."),
    ];

    public static void Show()
    {
        var res = Application.Current.Resources;
        var window = new Window
        {
            Title = "Como usar o RKZFPS",
            Width = 560,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = Application.Current.MainWindow is { IsLoaded: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = Application.Current.MainWindow is { IsLoaded: true } m ? m : null,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)res["Surface"],
        };

        var index = 0;
        var glyph = new TextBlock { FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 34, Foreground = (Brush)res["Accent"], Margin = new Thickness(0, 0, 0, 14) };
        var title = new TextBlock { Style = (Style)res["H2"], FontSize = 20 };
        var text = new TextBlock { Foreground = (Brush)res["Text"], LineHeight = 22, TextWrapping = TextWrapping.Wrap, MinHeight = 90 };
        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var skip = new Button { Content = "Pular", Style = (Style)res["Ghost"] };
        var back = new Button { Content = "Voltar", Margin = new Thickness(8, 0, 0, 0) };
        var next = new Button { Content = "Próximo", Style = (Style)res["Primary"], Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        var simple = new Button { Content = "Modo simples", Style = (Style)res["Primary"], Margin = new Thickness(8, 0, 0, 0) };
        var advanced = new Button { Content = "Modo avançado", Margin = new Thickness(8, 0, 0, 0) };

        void Render()
        {
            var s = Steps[index];
            glyph.Text = s.Glyph;
            title.Text = s.Title;
            text.Text = s.Text;
            dots.Children.Clear();
            for (var i = 0; i < Steps.Length; i++)
                dots.Children.Add(new Border
                {
                    Width = i == index ? 22 : 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 6, 0),
                    Background = (Brush)res[i == index ? "Accent" : "Border"],
                });
            var last = index == Steps.Length - 1;
            back.Visibility = index == 0 ? Visibility.Collapsed : Visibility.Visible;
            next.Visibility = last ? Visibility.Collapsed : Visibility.Visible;
            simple.Visibility = advanced.Visibility = last ? Visibility.Visible : Visibility.Collapsed;
            skip.Visibility = last ? Visibility.Collapsed : Visibility.Visible;
        }

        void Finish(bool? advancedMode)
        {
            var host = AppHost.Current;
            if (advancedMode is { } a)
                host.SetAdvancedMode(a);
            host.Ctx.Storage.SaveSettings(host.Ctx.Settings with { TutorialDone = true });
            window.Close();
        }

        next.Click += (_, _) => { index++; Render(); };
        back.Click += (_, _) => { index--; Render(); };
        skip.Click += (_, _) => Finish(null);
        simple.Click += (_, _) => Finish(false);
        advanced.Click += (_, _) => Finish(true);
        window.Closed += (_, _) =>
        {
            // Fechar pelo X também conta como visto: o guia não volta sozinho.
            var host = AppHost.Current;
            if (!host.Ctx.Settings.TutorialDone)
                host.Ctx.Storage.SaveSettings(host.Ctx.Settings with { TutorialDone = true });
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var b in new[] { skip, back, next, advanced, simple })
            buttons.Children.Add(b);
        var footer = new DockPanel { Margin = new Thickness(0, 20, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(dots);

        var root = new StackPanel { Margin = new Thickness(30) };
        root.Children.Add(glyph);
        root.Children.Add(title);
        root.Children.Add(text);
        root.Children.Add(footer);
        window.Content = root;
        Render();
        window.ShowDialog();
    }
}
