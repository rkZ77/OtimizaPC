using System.Text.RegularExpressions;

namespace Rkzfps.Core.Games;

/// <summary>
/// Leitura dos arquivos de texto da Steam (libraryfolders.vdf, appmanifest,
/// cs2_video.txt). Só leitura: o RKZFPS não reescreve config de jogo no MVP,
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

    /// <summary>
    /// Troca o valor de uma chave existente preservando o resto do arquivo
    /// (tabulação, ordem, outras chaves). Retorna null se a chave não existe:
    /// o RKZFPS não inventa chave nova no arquivo do jogo.
    /// </summary>
    public static string? ReplaceValue(string text, string key, string value)
    {
        var pattern = new Regex("^(\\s*\"" + Regex.Escape(key) + "\"\\s+\")((?:[^\"\\\\]|\\\\.)*)(\")", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        var replaced = false;
        var result = pattern.Replace(text, m =>
        {
            replaced = true;
            return m.Groups[1].Value + value + m.Groups[3].Value;
        });
        return replaced ? result : null;
    }

    public static IReadOnlyList<string> ParseLibraryFolders(string text) =>
        LibraryPath().Matches(text).Select(m => Unescape(m.Groups[1].Value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string Unescape(string value) => value.Replace("\\\\", "\\");
}
