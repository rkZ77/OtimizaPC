using System.Text;
using Rkzfps.Core.Games;

namespace Rkzfps.Windows;

/// <summary>
/// Onde fica o arquivo de configuração de cada fonte de perfil. O caminho
/// relativo vem do perfil JSON, então é conferido para não sair da pasta base:
/// um perfil adulterado com "..\..\" não aponta para arquivo do sistema.
/// </summary>
public static class GameLocator
{
    public static string? ConfigPath(GameConfigSource source) => source.Source switch
    {
        "steam_userdata" => SteamLocator.FindUserdataFile(source.RelativePath),
        "local_appdata" => Inside(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), source.RelativePath),
        "roaming_appdata" => Inside(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), source.RelativePath),
        "riot_lol" => LolInstall() is { } lol ? Inside(lol, source.RelativePath) : null,
        _ => null,
    };

    /// <summary>
    /// Pasta do League of Legends: a que o instalador da Riot registra no
    /// Windows, ou a padrão. Só leitura de registro, nada é gravado.
    /// </summary>
    private static string? LolInstall()
    {
        const string uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Riot Game league_of_legends.live";
        foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey(uninstall);
                if (key?.GetValue("InstallLocation") is string path && Directory.Exists(path))
                    return path;
            }
            catch (System.Security.SecurityException)
            {
            }
        }
        const string fallback = @"C:\Riot Games\League of Legends";
        return Directory.Exists(fallback) ? fallback : null;
    }

    private static string? Inside(string root, string relative)
    {
        if (string.IsNullOrEmpty(root))
            return null;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', '\\')));
        var baseDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase) && File.Exists(full) ? full : null;
    }
}

/// <summary>
/// Lê e grava texto mantendo a codificação original. Alguns jogos gravam a
/// config em UTF-16 ou com BOM; regravar em outra codificação pode fazer o
/// jogo ignorar o arquivo e voltar tudo ao padrão.
/// </summary>
public static class TextFiles
{
    public static (string Text, Encoding Encoding) Read(string path)
    {
        using var reader = new StreamReader(path, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        return (text, reader.CurrentEncoding);
    }

    /// <summary>
    /// Cópia do arquivo original, uma vez só, antes da primeira alteração.
    /// Versões antigas gravavam a cópia como .fpsx-original: se ela existe, o
    /// original já está guardado, e copiar de novo guardaria o arquivo já alterado.
    /// </summary>
    public static void KeepOriginal(string path)
    {
        if (File.Exists(path + ".fpsx-original") || File.Exists(path + ".rkzfps-original"))
            return;
        File.Copy(path, path + ".rkzfps-original");
    }

    /// <summary>Grava num temporário e troca: o arquivo nunca fica pela metade.</summary>
    public static void Write(string path, string text, Encoding encoding)
    {
        var tmp = path + ".rkzfps-tmp";
        File.WriteAllText(tmp, text, encoding);
        File.Move(tmp, path, overwrite: true);
    }
}
