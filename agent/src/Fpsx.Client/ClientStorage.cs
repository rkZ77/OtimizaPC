using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fpsx.Core.Json;

namespace Fpsx.Client;

public sealed record ClientSettings
{
    /// <summary>URL da API em produção. Atualizar aqui quando o domínio definitivo for configurado no Railway.</summary>
    public const string ProductionApiUrl = "https://otimizapc-production.up.railway.app";

    public string ApiUrl { get; init; } = Environment.GetEnvironmentVariable("FPSX_API_URL") ?? ProductionApiUrl;

    /// <summary>null = ainda não perguntado. Telemetria só sai com true (seção 40).</summary>
    public bool? TelemetryConsent { get; init; }

    public string Profile { get; init; } = "gaming";
    public string? PresentMonPath { get; init; }

    /// <summary>Medir o FPS das partidas sozinho, com o app aberto ou na bandeja.</summary>
    public bool AutoMeasure { get; init; } = true;

    /// <summary>A tela de entrada da primeira abertura já apareceu (entrou ou escolheu o Free).</summary>
    public bool LoginPromptDone { get; init; }
}

/// <summary>Arquivos do app em %LOCALAPPDATA%\FPSX. Nada disso sai do PC sem ação do usuário.</summary>
public sealed class ClientStorage(string dataDir)
{
    private string SettingsPath => Path.Combine(dataDir, "settings.json");
    private string TokenPath => Path.Combine(dataDir, "license.token");
    private string OverridesPath => Path.Combine(dataDir, "catalog-overrides.token");
    private string QueuePath => Path.Combine(dataDir, "telemetry-queue.jsonl");

    public ClientSettings LoadSettings()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(SettingsPath), FpsxJson.Options) ?? new ClientSettings()
                : new ClientSettings();
        }
        catch (JsonException)
        {
            return new ClientSettings();
        }
    }

    public void SaveSettings(ClientSettings settings) => WriteAtomic(SettingsPath, JsonSerializer.Serialize(settings, FpsxJson.Options));

    // O token é assinado (ninguém consegue mudar o plano dele), mas também
    // autentica o PC na API. DPAPI amarra o arquivo ao usuário do Windows:
    // copiado para outro PC ou outra conta, ele não abre.
    public string? LoadToken()
    {
        if (!File.Exists(TokenPath))
            return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(TokenPath), null, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void SaveToken(string token)
    {
        Directory.CreateDirectory(dataDir);
        var tmp = TokenPath + ".tmp";
        File.WriteAllBytes(tmp, ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));
        File.Move(tmp, TokenPath, overwrite: true);
    }

    public void DeleteToken()
    {
        if (File.Exists(TokenPath))
            File.Delete(TokenPath);
    }

    public string? LoadOverrides() => File.Exists(OverridesPath) ? File.ReadAllText(OverridesPath) : null;

    public void SaveOverrides(string token) => WriteAtomic(OverridesPath, token);

    public void Enqueue(TelemetryEvent e)
    {
        Directory.CreateDirectory(dataDir);
        File.AppendAllText(QueuePath, JsonSerializer.Serialize(e, FpsxJson.Compact) + Environment.NewLine);
    }

    public IReadOnlyList<TelemetryEvent> PeekQueue(int max) =>
        File.Exists(QueuePath)
            ? File.ReadLines(QueuePath).Take(max)
                .Select(l => { try { return JsonSerializer.Deserialize<TelemetryEvent>(l, FpsxJson.Options); } catch (JsonException) { return null; } })
                .OfType<TelemetryEvent>().ToList()
            : [];

    public void DropFromQueue(int count)
    {
        if (!File.Exists(QueuePath))
            return;
        var rest = File.ReadLines(QueuePath).Skip(count).ToList();
        WriteAtomic(QueuePath, rest.Count == 0 ? "" : string.Join(Environment.NewLine, rest) + Environment.NewLine);
    }

    public void ClearQueue()
    {
        if (File.Exists(QueuePath))
            File.Delete(QueuePath);
    }

    private void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(dataDir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}
