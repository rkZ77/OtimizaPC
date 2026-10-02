using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rkzfps.Core.Json;

namespace Rkzfps.Client;

public sealed record ClientSettings
{
    /// <summary>URL da API em produção: o domínio próprio, que não muda se o serviço do Railway for recriado.</summary>
    public const string ProductionApiUrl = "https://rkzfps.com.br";

    /// <summary>
    /// Endereço da API. NÃO é salvo nas configurações: uma versão antiga
    /// gravava o endereço provisório do projeto e todo PC que tinha esse
    /// arquivo ficou sem conseguir entrar. Vem do app (ou de RKZFPS_API_URL em
    /// desenvolvimento), e o valor salvo por versões antigas é ignorado.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ApiUrl => Environment.GetEnvironmentVariable("RKZFPS_API_URL") ?? ProductionApiUrl;

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

    /// <summary>
    /// Perfil interno do catálogo. Com <see cref="Preferences"/> preenchido, só
    /// vale quando a escolha é Avançado (ex.: streaming); sem ele, é o perfil
    /// de sempre, e quem atualiza o app continua exatamente como estava.
    /// </summary>
    public string Profile { get; init; } = "gaming";

    /// <summary>
    /// Respostas das perguntas da primeira abertura e o perfil da tela. null =
    /// nunca respondeu nem pulou (instalação antiga). Pular grava um
    /// UserPreferences vazio, que é o Automático. Nunca sai do PC.
    /// </summary>
    public Rkzfps.Core.Engine.UserPreferences? Preferences { get; init; }

    /// <summary>Perguntas só em instalação nova: quem já usava o app segue com o perfil que tinha.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool NeedsOnboarding => Preferences is null && !TutorialDone;

    public string? PresentMonPath { get; init; }

    /// <summary>Medir o FPS das partidas sozinho, com o app aberto ou na bandeja.</summary>
    public bool AutoMeasure { get; init; } = true;

    /// <summary>
    /// Modo Gaming: "manual" (padrão, nada é aplicado sozinho) ou "auto"
    /// (aplica ao abrir o jogo só o que está em GamingAuthorized e desfaz ao fechar).
    /// </summary>
    public string GamingMode { get; init; } = "manual";

    /// <summary>Meta de FPS por jogo (id do jogo -> FPS). Sem entrada = sem meta.</summary>
    public IReadOnlyDictionary<string, int> FpsGoals { get; init; } = new Dictionary<string, int>();

    /// <summary>Atalho Ctrl+Shift+F para o painel de FPS por cima do jogo. Desligável: pode colidir com atalho de algum jogo.</summary>
    public bool OverlayHotkey { get; init; } = true;

    /// <summary>Tema do app: "dark" (padrão) ou "light". Só muda quando a pessoa escolhe, igual ao site.</summary>
    public string Theme { get; init; } = "dark";

    /// <summary>Otimizações que a pessoa autorizou para o modo Automático (ids do catálogo).</summary>
    public IReadOnlyList<string> GamingAuthorized { get; init; } = [];

    /// <summary>A tela de entrada da primeira abertura já apareceu (entrou ou escolheu o Free).</summary>
    public bool LoginPromptDone { get; init; }

    /// <summary>"simple" (padrão: o essencial, botões grandes) ou "advanced" (tudo, item a item).</summary>
    public string Mode { get; init; } = "simple";

    /// <summary>O tutorial da primeira abertura já foi visto (ou pulado).</summary>
    public bool TutorialDone { get; init; }

    /// <summary>Avisos na bandeja: vigia do PC (atualização que desfez correção) e resumo da semana.</summary>
    public bool WatchNotify { get; init; } = true;

    /// <summary>Último resumo semanal mostrado: no máximo um a cada 7 dias.</summary>
    public DateTimeOffset? LastWeeklySummaryAt { get; init; }

    /// <summary>Versão cujas novidades a pessoa já viu. null = instalação nova, sem tela de novidades.</summary>
    public string? LastSeenVersion { get; init; }
}

/// <summary>Arquivos do app em %LOCALAPPDATA%\RKZFPS. Nada disso sai do PC sem ação do usuário.</summary>
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
                ? JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(SettingsPath), RkzfpsJson.Options) ?? new ClientSettings()
                : new ClientSettings();
        }
        catch (JsonException)
        {
            return new ClientSettings();
        }
    }

    public void SaveSettings(ClientSettings settings) => WriteAtomic(SettingsPath, JsonSerializer.Serialize(settings, RkzfpsJson.Options));

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
        File.AppendAllText(QueuePath, JsonSerializer.Serialize(e, RkzfpsJson.Compact) + Environment.NewLine);
    }

    public IReadOnlyList<TelemetryEvent> PeekQueue(int max) =>
        File.Exists(QueuePath)
            ? File.ReadLines(QueuePath).Take(max)
                .Select(l => { try { return JsonSerializer.Deserialize<TelemetryEvent>(l, RkzfpsJson.Options); } catch (JsonException) { return null; } })
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
