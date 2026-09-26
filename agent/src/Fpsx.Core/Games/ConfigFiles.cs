using System.Text;
using System.Text.RegularExpressions;

namespace Fpsx.Core.Games;

/// <summary>
/// Leitura e troca de valor nos formatos de configuração de jogo. Troca só
/// valor de chave que JÁ existe, preservando o resto do arquivo: o FPSX não
/// inventa chave nova num arquivo que o jogo controla.
/// </summary>
public static class ConfigFiles
{
    public const string ValveKv = "valve_kv";
    public const string Ini = "ini";
    public const string ColonKv = "colon_kv";

    public static Dictionary<string, string> Parse(string format, string text) => format switch
    {
        ValveKv => ValveFiles.ParseFlatKeyValues(text),
        Ini => ParseIni(text),
        ColonKv => ParseColon(text),
        _ => throw new NotSupportedException($"Formato de configuração desconhecido: {format}"),
    };

    public static string? Replace(string format, string text, string key, string value) => format switch
    {
        ValveKv => ValveFiles.ReplaceValue(text, key, value),
        Ini => ReplaceIni(text, key, value),
        ColonKv => ReplaceColon(text, key, value),
        _ => throw new NotSupportedException($"Formato de configuração desconhecido: {format}"),
    };

    // ---- ini: chave no FPSX é "Seção|Chave" (nome de chave pode ter ponto, como sg.ShadowQuality) ----

    private static Dictionary<string, string> ParseIni(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var section = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq > 0 && !line.StartsWith(';'))
                result[$"{section}|{line[..eq].Trim()}"] = line[(eq + 1)..].Trim();
        }

        return result;
    }

    private static string? ReplaceIni(string text, string key, string value)
    {
        var bar = key.IndexOf('|');
        if (bar < 0)
            return null;
        var (wantSection, wantKey) = (key[..bar], key[(bar + 1)..]);

        var lines = text.Split('\n');
        var section = "";
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                section = trimmed[1..^1];
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq > 0 && section.Equals(wantSection, StringComparison.OrdinalIgnoreCase)
                && line[..eq].Trim().Equals(wantKey, StringComparison.OrdinalIgnoreCase))
            {
                var cr = lines[i].EndsWith('\r') ? "\r" : "";
                lines[i] = line[..(eq + 1)] + value + cr;
                return string.Join('\n', lines);
            }
        }

        return null;
    }

    // ---- colon_kv: options.txt do Minecraft (chave:valor, sem seções) ----

    private static Dictionary<string, string> ParseColon(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var colon = line.IndexOf(':');
            if (colon > 0)
                result[line[..colon]] = line[(colon + 1)..];
        }

        return result;
    }

    private static string? ReplaceColon(string text, string key, string value)
    {
        var pattern = new Regex("^" + Regex.Escape(key) + ":[^\\r\\n]*", RegexOptions.Multiline);
        var replaced = false;
        var result = pattern.Replace(text, _ =>
        {
            replaced = true;
            return key + ":" + value;
        }, 1);
        return replaced ? result : null;
    }
}
