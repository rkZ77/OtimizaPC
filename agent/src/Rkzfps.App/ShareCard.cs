using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rkzfps.Core.Benchmark;

namespace Rkzfps.App;

/// <summary>
/// Imagem de uma partida para postar no grupo: só números medidos, o gráfico
/// e a placa de vídeo (sem ela, FPS não diz nada a quem vê). Nada de
/// "ganhou X%" sem comparação real: o card é da partida, não uma promessa.
/// </summary>
public static class ShareCard
{
    public const int Width = 1200;
    public const int Height = 630;

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Gera o PNG, salva em Imagens\RKZFPS e copia para a área de transferência. Devolve o caminho.</summary>
    public static string Export(GameplaySession s, IReadOnlyList<DropCause> drops, string causes, string? siteLink = null)
    {
        var card = Build(s, drops, causes, siteLink ?? "rkzfps.com.br");
        card.Measure(new Size(Width, Height));
        card.Arrange(new Rect(0, 0, Width, Height));
        card.UpdateLayout();

        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(card);
        bitmap.Freeze();

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "RKZFPS");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{s.GameId}-{s.StartedAt.ToLocalTime():yyyyMMdd-HHmm}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
            encoder.Save(file);

        try
        {
            Clipboard.SetImage(bitmap);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Outro programa segurando a área de transferência: o arquivo já está salvo.
        }

        return path;
    }

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private static TextBlock Text(string text, double size, string brush = "Text", FontWeight? weight = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = Res(brush),
        FontWeight = weight ?? FontWeights.Normal,
        FontFamily = new FontFamily("Segoe UI"),
        TextWrapping = TextWrapping.Wrap,
    };

    private static FrameworkElement Stat(string value, string label) => new StackPanel
    {
        Margin = new Thickness(0, 0, 48, 0),
        Children = { Text(value, 44, "Text", FontWeights.Bold), Text(label, 16, "Muted") },
    };

    private static FrameworkElement Build(GameplaySession s, IReadOnlyList<DropCause> drops, string causes, string siteLink)
    {
        var root = new Border
        {
            Width = Width,
            Height = Height,
            Background = Res("Bg"),
            Padding = new Thickness(56, 44, 56, 36),
        };
        var stack = new DockPanel { LastChildFill = true };
        root.Child = stack;

        // Cabeçalho: ícone do jogo, nome e data.
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 24) };
        DockPanel.SetDock(header, Dock.Top);
        var brand = new TextBlock { FontSize = 24, FontWeight = FontWeights.Black, FontStyle = FontStyles.Italic, FontFamily = new FontFamily("Segoe UI") };
        brand.Inlines.Add(new System.Windows.Documents.Run("RKZ") { Foreground = Res("Text") });
        brand.Inlines.Add(new System.Windows.Documents.Run("FPS") { Foreground = Res("Accent") });
        brand.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(brand, Dock.Right);
        header.Children.Add(brand);
        if (GameIcons.Get(s.GameId) is { } icon)
        {
            var img = new Image { Source = icon, Width = 48, Height = 48, Margin = new Thickness(0, 0, 16, 0) };
            DockPanel.SetDock(img, Dock.Left);
            header.Children.Add(img);
        }

        header.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Text(s.GameName, 28, "Text", FontWeights.SemiBold),
                Text($"{s.StartedAt.ToLocalTime():dd/MM/yyyy}, {s.MeasuredSeconds / 60:0} min medidos em partida real", 15, "Muted"),
            },
        });
        stack.Children.Add(header);

        // Números.
        var stats = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 20) };
        DockPanel.SetDock(stats, Dock.Top);
        stats.Children.Add(Stat(s.Stats.AvgFps.ToString("0", Pt), "FPS médio"));
        stats.Children.Add(Stat(s.Stats.Low1Fps.ToString("0", Pt), "1% low"));
        stats.Children.Add(Stat(drops.Count.ToString(Pt), drops.Count == 1 ? "queda forte" : "quedas fortes"));
        stack.Children.Add(stats);

        // Rodapé: hardware e de onde vem o número.
        var gpu = s.Hardware?.Gpu is { Length: > 0 } g ? g : "placa de vídeo não identificada";
        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        // Com conta, o link é o de indicação: quem vê a imagem e assina rende dias para quem compartilhou.
        var site = Text($"Azul: FPS médio. Laranja: pior quadro. Medido com RKZFPS, {siteLink}", 15, "Muted");
        DockPanel.SetDock(site, Dock.Right);
        footer.Children.Add(site);
        footer.Children.Add(Text(gpu + (s.Hardware is { Threads: > 0 } h ? $", {h.Threads} threads, {h.RamGb:0} GB de RAM" : ""), 15, "Muted"));
        stack.Children.Add(footer);

        if (causes.Length > 0)
        {
            var note = Text(causes, 15, "Text");
            note.Margin = new Thickness(0, 12, 0, 0);
            DockPanel.SetDock(note, Dock.Bottom);
            stack.Children.Add(note);
        }

        stack.Children.Add(new FpsChart { Points = s.Timeline, MonitorHz = s.DisplayHz, Drops = drops, Height = double.NaN });
        return root;
    }
}
