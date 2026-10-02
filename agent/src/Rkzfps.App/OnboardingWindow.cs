using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Rkzfps.Core.Engine;

namespace Rkzfps.App;

/// <summary>
/// Primeira abertura: enquanto o scan roda, no máximo três perguntas de
/// preferência, todas puláveis. Não é cadastro: o hardware vem do scan, e
/// pular usa o Automático. Tudo local, nenhuma chamada de IA.
/// </summary>
public static class OnboardingWindow
{
    public static void Show()
    {
        var host = AppHost.Current;
        var res = Application.Current.Resources;
        var window = new Window
        {
            Title = "RKZFPS",
            Width = 600,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = Application.Current.MainWindow is { IsLoaded: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = Application.Current.MainWindow is { IsLoaded: true } m ? m : null,
            ResizeMode = ResizeMode.NoResize,
        };
        window.SetResourceReference(Control.BackgroundProperty, "Surface");

        var prefs = new UserPreferences();
        var saved = false;
        var queue = new Queue<OnboardingQuestion>();
        var contextualLoaded = false;
        var waitingScan = false;

        var title = new TextBlock { Style = (Style)res["H1"], FontSize = 22, Margin = new Thickness(0, 0, 0, 6) };
        var subtitle = new TextBlock { Style = (Style)res["MutedText"], Margin = new Thickness(0, 0, 0, 18) };
        var hardware = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        var question = new TextBlock { Style = (Style)res["H2"], Margin = new Thickness(0, 0, 0, 12) };
        var options = new UniformGrid { Columns = 2 };
        var reasons = new StackPanel();
        var skip = new Button { Content = "Pular", Style = (Style)res["Ghost"] };
        var games = new Button { Content = "Escolher um jogo", Style = (Style)res["Secondary"], Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed };
        var done = new Button { Content = "Continuar", Style = (Style)res["Primary"], Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed, IsDefault = true };

        void Save()
        {
            if (saved)
                return;
            saved = true;
            host.SetPreferences(prefs);
            // O guia antigo não aparece por cima: ele continua em "Como usar o RKZFPS".
            host.Ctx.Storage.SaveSettings(host.Ctx.Settings with { TutorialDone = true });
        }

        void RenderHardware()
        {
            hardware.Children.Clear();
            if (host.Report?.Hardware is not { } hw || host.Scan is not { } scan)
                return;
            var hz = Onboarding.MaxRefreshHz(scan.Snapshot);
            var lines = new List<(string, string)>
            {
                ("Processador", hw.GetValueOrDefault("CPU", "")),
                ("Placa de vídeo", hw.GetValueOrDefault("GPU", "")),
                ("Memória", hw.GetValueOrDefault("RAM", "")),
                ("Disco", hw.GetValueOrDefault("Armazenamento", "")),
            };
            if (hz > 0)
                lines.Add(("Monitor", $"{hz} Hz"));
            lines.Add(("Sistema", hw.GetValueOrDefault("Windows", "")));
            var card = new Border { Style = (Style)res["Card"], Margin = new Thickness(0) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var row = 0;
            foreach (var (label, value) in lines.Where(l => l.Item2.Length > 0))
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = new TextBlock { Text = label, Style = (Style)res["MutedText"], Margin = new Thickness(0, 2, 0, 2) };
                var v = new TextBlock { Text = value, Margin = new Thickness(0, 2, 0, 2) };
                Grid.SetRow(l, row);
                Grid.SetRow(v, row);
                Grid.SetColumn(v, 1);
                grid.Children.Add(l);
                grid.Children.Add(v);
                row++;
            }
            card.Child = grid;
            hardware.Children.Add(new TextBlock { Text = "Encontramos seu PC.", Style = (Style)res["H2"] });
            hardware.Children.Add(card);
        }

        void Ask(OnboardingQuestion q)
        {
            question.Text = q.Title;
            question.Visibility = Visibility.Visible;
            options.Children.Clear();
            options.Visibility = Visibility.Visible;
            options.Columns = q.Options.Count > 2 ? 2 : q.Options.Count;
            foreach (var o in q.Options)
            {
                var b = new Button { Content = o.Label, Style = (Style)res["Secondary"], Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 12, 14, 12), FontSize = 15 };
                b.Click += (_, _) =>
                {
                    prefs = Onboarding.Answer(prefs, q.Id, o.Value);
                    Next();
                };
                options.Children.Add(b);
            }
        }

        void Next()
        {
            if (!contextualLoaded)
            {
                if (host.Scan is not { } scan)
                {
                    // As perguntas de contexto dependem do hardware: espera o scan,
                    // com o Pular sempre disponível.
                    waitingScan = true;
                    question.Text = "Analisando seu PC...";
                    options.Visibility = Visibility.Collapsed;
                    return;
                }
                contextualLoaded = true;
                foreach (var q in Onboarding.Contextual(scan.Snapshot, prefs))
                    queue.Enqueue(q);
            }
            if (queue.Count > 0)
            {
                Ask(queue.Dequeue());
                return;
            }
            Finish();
        }

        void Finish()
        {
            Save();
            var tuning = host.Tuning;
            title.Text = "Tudo pronto";
            subtitle.Text = tuning?.Summary ?? "";
            // No fim a frase é a resposta para a pessoa: texto normal, não legenda.
            subtitle.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            subtitle.FontSize = 15;
            question.Visibility = Visibility.Collapsed;
            options.Visibility = Visibility.Collapsed;
            reasons.Children.Clear();
            foreach (var r in tuning?.Reasons ?? [])
                reasons.Children.Add(new TextBlock { Text = r, Margin = new Thickness(0, 0, 0, 8), LineHeight = 21 });
            reasons.Children.Add(new TextBlock { Text = "Dá para trocar o perfil quando quiser em Configurações.", Style = (Style)res["MutedText"], Margin = new Thickness(0, 4, 0, 0) });
            skip.Visibility = Visibility.Collapsed;
            games.Visibility = done.Visibility = Visibility.Visible;
        }

        void OnHost(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AppHost.Scan))
                return;
            RenderHardware();
            if (!saved)
                Subtitle();
            if (waitingScan && host.Scan is not null)
            {
                waitingScan = false;
                Next();
            }
        }

        host.PropertyChanged += OnHost;
        skip.Click += (_, _) => window.Close();
        done.Click += (_, _) => window.Close();
        games.Click += (_, _) =>
        {
            window.Close();
            host.Navigate<ViewModels.GamesViewModel>();
        };
        window.Closed += (_, _) =>
        {
            host.PropertyChanged -= OnHost;
            // Pular ou fechar pelo X guarda o que já foi respondido; sem
            // resposta nenhuma, fica o Automático.
            Save();
        };

        title.Text = "Vamos descobrir como otimizar seu PC.";
        // A análise pode terminar antes de a janela abrir: a frase acompanha.
        void Subtitle() => subtitle.Text = host.Scan is null
            ? "Enquanto o RKZFPS analisa o seu hardware, responda rapidinho. Se preferir, pule: ele decide sozinho com segurança."
            : "Responda rapidinho para o RKZFPS saber o que você prefere. Se preferir, pule: ele decide sozinho com segurança.";
        Subtitle();
        RenderHardware();
        Ask(Onboarding.Main);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var b in new[] { skip, games, done })
            buttons.Children.Add(b);

        var root = new StackPanel { Margin = new Thickness(30, 0, 30, 30) };
        foreach (var e in new UIElement[] { title, subtitle, hardware, question, options, reasons, buttons })
            root.Children.Add(e);
        ThemedWindow.Apply(window, root);
        window.ShowDialog();
    }
}
