using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Fpsx.App;

/// <summary>
/// Ícone de cada jogo, tirado do próprio executável instalado no PC. O RKZFPS
/// não distribui arte de ninguém: mostra o ícone que o jogo já tem, e guarda
/// uma cópia em PNG para não reabrir o .exe a cada tela.
/// </summary>
public static class GameIcons
{
    private static readonly Dictionary<string, ImageSource?> Memory = new(StringComparer.OrdinalIgnoreCase);

    private static string Dir => Path.Combine(AppHost.Current.Ctx.DataDir, "icons");

    private static string CachePath(string gameId) => Path.Combine(Dir, gameId + ".png");

    /// <summary>Ícone do jogo, do cache ou do executável. null = ainda não visto (a tela mostra as iniciais).</summary>
    public static ImageSource? Get(string gameId, string? exePath = null)
    {
        if (Memory.TryGetValue(gameId, out var known) && known is not null)
            return known;
        var icon = Load(CachePath(gameId)) ?? (exePath is not null ? Extract(gameId, exePath) : null);
        Memory[gameId] = icon;
        return icon;
    }

    /// <summary>Guarda o ícone de um jogo visto rodando (os que não se instalam pela Steam).</summary>
    public static void Remember(string gameId, string exePath)
    {
        if (!File.Exists(CachePath(gameId)))
            Extract(gameId, exePath);
    }

    private static ImageSource? Load(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.UriSource = new Uri(path);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static ImageSource? Extract(string gameId, string exePath)
    {
        if (!File.Exists(exePath))
            return null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon is null)
                return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            Directory.CreateDirectory(Dir);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var file = File.Create(CachePath(gameId)))
                encoder.Save(file);
            Memory[gameId] = source;
            return source;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Até duas letras do nome, para o quadrado de quem ainda não tem ícone.</summary>
    public static string Initials(string name)
    {
        var words = name.Split([' ', ':', '-'], StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetterOrDigit(w[0])).ToList();
        return words.Count switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}",
        };
    }
}
