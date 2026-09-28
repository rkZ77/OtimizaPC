using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fpsx.Core.Json;

namespace Fpsx.Client;

public sealed record ClientSettings
{
    /// <summary>URL da API em produção. Atualizar aqui quando o domínio definitivo for configurado no Railway.</summary>
    public const string ProductionApiUrl = "https://otimizapc-production.up.railway.app";

    /// <summary>
    /// Endereço da API. NÃO é salvo nas configurações: uma versão antiga
    /// gravava o endereço provisório do projeto e todo PC que tinha esse
    /// arquivo ficou sem conseguir entrar. Vem do app (ou de FPSX_API_URL em
    /// desenvolvimento), e o valor salvo por versões antigas é ignorado.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ApiUrl => Environment.GetEnvironmentVariable("FPSX_API_URL") ?? ProductionApiUrl;

    /// <summary>null = ainda não perguntado. Telemetria só sai com true (seção 40).</summary>
    public bool? TelemetryConsent { get; init; }

    /// <summary>
    /// Qual texto de consentimento a pessoa aceitou. A versão 2 inclui as
    /// partidas (FPS, gráfico e resumo do hardware). Quem aceitou a versão 1
    /// não aceitou isso: o app pergunta de novo e, até lá, partida não sobe.
    /// </summary>
    public int TelemetryConsentVersion { get; init; }

    public const int CurrentConsentVersion = 2;

    /// <summary>Pode subir as partidas: aceitou o texto que fala delas.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool GameplayConsent => TelemetryConsent == true && TelemetryConsentVersion >= CurrentConsentVersion;

    public string Profile { get; init; } = "gaming";
    public string? PresentMonPath { get; init; }

    /// <summary>Medir o FPS das partidas sozinho, com o app aberto ou na bandeja.</summary>
    public bool AutoMeasure { get; init; } = true;

    /// <summary>A tela de entrada da primeira abertura já apareceu (entrou ou escolheu o Free).</summary>
    public bool LoginPromptDone { get; init; }

    /// <summary>"simple" (padrão: o essencial, botões grandes) ou "advanced" (tudo, item a item).</summary>
    public string Mode { get; init; } = "simple";

    /// <summary>O tutorial da primeira abertura já foi visto (ou pulado).</summary>
    public bool TutorialDone { get; init; }
}

/// <summary>Arquivos do app em %LOCALAPPDATA%\FPSX. Nada disso sai do PC sem ação do usuário.</summary>
public sealed class ClientStorage(string dataDir)
{
    private string SettingsPath => Path.Combine(dataDir, "settings.json");
    private string TokenPath => Path.Combine(dataDir, "license.token");
    private string OverridesPath => Path.Combine(dataDir, "catalog-overrides.token");
    private string QueuePath => Path.Combine(dataDir, "telemetry-queue.jsonl");
    private string UploadedPath => Path.Combine(dataDir, "gameplay-uploaded.txt");

    /// <summary>Partidas que o servidor já recebeu (uma por linha).</summary>
    public HashSet<string> UploadedGameplay() =>
        File.Exists(UploadedPath) ? File.ReadLines(UploadedPath).Where(l => l.Length > 0).ToHashSet(StringComparer.Ordinal) : [];

    public void MarkGameplayUploaded(string sessionId)
    {
        Directory.CreateDirectory(dataDir);
        File.AppendAllText(UploadedPath, sessionId + Environment.NewLine);
    }

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
        e = e with { OccurredAt = e.OccurredAt ?? DateTimeOffset.UtcNow, AgentVersion = e.AgentVersion ?? AgentContext.Version };
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
