using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fpsx.Core.Model;

namespace Fpsx.Windows;

/// <summary>
/// settings.json do Discord (versão estável). Lê e grava só a chave pedida e
/// mantém o resto do arquivo como está: janela, áudio e opções de vídeo da
/// pessoa não são tocados.
/// </summary>
public static class DiscordSettings
{
    public static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "discord", "settings.json");

    public static bool IsRunning() => Process.GetProcessesByName("Discord").Length > 0;

    public static DiscordInfo? Read(IReadOnlyList<StartupEntry> startup)
    {
        if (!File.Exists(Path))
            return null;
        return new DiscordInfo
        {
            Running = IsRunning(),
            HardwareAcceleration = ReadValue("enableHardwareAcceleration") != "false",
            OpensWithWindows = startup.Any(s => s.Enabled && s.Name.Contains("discord", StringComparison.OrdinalIgnoreCase)),
        };
    }

    /// <summary>Texto do valor ("true"/"false"). Chave ausente devolve o padrão do Discord.</summary>
    public static string? ReadValue(string key)
    {
        if (!File.Exists(Path))
            return null;
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(Path)) as JsonObject;
            if (root?[key] is JsonValue v && v.TryGetValue<bool>(out var b))
                return b ? "true" : "false";
            return key == "enableHardwareAcceleration" ? "true" : null;
        }
        catch (JsonException)
        {
            // Arquivo corrompido: não dá para afirmar nada, e o FPSX não escreve por cima.
            return null;
        }
    }

    public static void Write(string key, string value)
    {
        if (!File.Exists(Path))
            throw new InvalidOperationException("O Discord não está instalado nesta conta do Windows.");
        var text = File.ReadAllText(Path);
        var root = JsonNode.Parse(text) as JsonObject
                   ?? throw new InvalidOperationException("O arquivo de configuração do Discord não está no formato esperado.");
        root[key] = value == "true";

        // Cópia do arquivo original uma vez só, além do inverso que o motor guarda.
        var backup = Path + ".fpsx-original";
        if (!File.Exists(backup))
            File.Copy(Path, backup);

        File.WriteAllText(Path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }
}
