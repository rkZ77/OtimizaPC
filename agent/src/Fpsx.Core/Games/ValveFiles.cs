using System.Text.RegularExpressions;

namespace Fpsx.Core.Games;

/// <summary>
/// Leitura dos arquivos de texto da Steam (libraryfolders.vdf, appmanifest,
/// cs2_video.txt). Só leitura: o FPSX não reescreve config de jogo no MVP,
/// porque o próprio jogo sobrescreve o arquivo ao fechar e a alteração sumiria
/// sem o usuário saber.
/// </summary>
public static partial class ValveFiles
{
    [GeneratedRegex("^\\s*\"([^\"]+)\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Multiline)]
    private static partial Regex KeyValueLine();

    [GeneratedRegex("\"path\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPath();

    /// <summary>Pares chave/valor de um arquivo KV plano. Chave repetida: vale a última.</summary>
    public static Dictionary<string, string> ParseFlatKeyValues(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in KeyValueLine().Matches(text))
            result[m.Groups[1].Value] = Unescape(m.Groups[2].Value);
        return result;
    }

    public static IReadOnlyList<string> ParseLibraryFolders(string text) =>
        LibraryPath().Matches(text).Select(m => Unescape(m.Groups[1].Value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string Unescape(string value) => value.Replace("\\\\", "\\");
}
