using System.Text.Json;
using Fpsx.Core.Json;
using Fpsx.Core.Model;

namespace Fpsx.Core.Engine;

public enum ChangeStatus
{
    Pending,
    Applied,
    Failed,
    RolledBack,
    RollbackSkipped,
    RollbackFailed,
}

public enum SessionStatus
{
    Running,
    Completed,
    Failed,
    Cancelled,
    RolledBack,
}

public sealed record ChangeRecord
{
    public string Id { get; init; } = "";
    public string OptimizationId { get; init; } = "";
    public string ProposalId { get; init; } = "";
    public Change Applied { get; init; } = null!;

    /// <summary>Alteração que devolve o estado anterior. null = não reversível.</summary>
    public Change? Inverse { get; init; }

    public ChangeStatus Status { get; init; }
    public bool Verified { get; init; }
    public string? Detail { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset At { get; init; }
    public DateTimeOffset? RolledBackAt { get; init; }
}

public sealed record SkippedItem(string ProposalId, string Reason);

public sealed record SessionRecord
{
    public string Id { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public string ProfileId { get; init; } = "";
    public string CatalogVersion { get; init; } = "";
    public string MachineName { get; init; } = "";
    public SessionStatus Status { get; init; }
    public IReadOnlyList<ChangeRecord> Changes { get; init; } = [];
    public IReadOnlyList<SkippedItem> Skipped { get; init; } = [];

    /// <summary>Otimizações que o scan encontrou já corretas: entram no histórico como "já estavam certas".</summary>
    public IReadOnlyList<string> AlreadyOptimal { get; init; } = [];

    public string? BenchmarkBeforeId { get; init; }
    public string? BenchmarkAfterId { get; init; }

    /// <summary>
    /// Sessão temporária do modo Gaming Automático: nome do jogo que a abriu.
    /// É desfeita quando o jogo fecha (ou na próxima abertura do app, se ele
    /// fechou no meio). null = sessão normal, que fica até a pessoa desfazer.
    /// </summary>
    public string? GamingGame { get; init; }
}

/// <summary>Uma linha do log de alterações (seção 44), em JSON Lines.</summary>
public sealed record ChangeLogEntry(
    DateTimeOffset Timestamp,
    string SessionId,
    string OptimizationId,
    string Action,
    Change Change,
    Change? BeforeState,
    bool Success,
    bool Verified,
    string? Detail,
    string? Error);

/// <summary>
/// BackupManager + histórico: uma sessão por arquivo, gravada a cada passo.
/// Arquivo por sessão (e não um banco) porque o rollback precisa funcionar
/// mesmo sem rede e sem o serviço do RKZFPS rodando.
/// </summary>
public sealed class SessionStore
{
    private readonly string _sessionsDir;
    private readonly string _logPath;
    private readonly object _lock = new();

    public SessionStore(string dataDirectory)
    {
        _sessionsDir = Path.Combine(dataDirectory, "sessions");
        var logDir = Path.Combine(dataDirectory, "log");
        Directory.CreateDirectory(_sessionsDir);
        Directory.CreateDirectory(logDir);
        _logPath = Path.Combine(logDir, "changes.jsonl");
    }

    public string LogPath => _logPath;

    public void Save(SessionRecord session)
    {
        var path = Path.Combine(_sessionsDir, session.Id + ".json");
        var tmp = path + ".tmp";
        // Escrita atômica: um backup pela metade é pior que nenhum.
        File.WriteAllText(tmp, JsonSerializer.Serialize(session, FpsxJson.Options));
        File.Move(tmp, path, overwrite: true);
    }

    public SessionRecord? Load(string id)
    {
        var path = Path.Combine(_sessionsDir, Path.GetFileName(id) + ".json");
        return File.Exists(path) ? JsonSerializer.Deserialize<SessionRecord>(File.ReadAllText(path), FpsxJson.Options) : null;
    }

    public IReadOnlyList<SessionRecord> All() =>
        Directory.EnumerateFiles(_sessionsDir, "*.json")
            .Select(f => JsonSerializer.Deserialize<SessionRecord>(File.ReadAllText(f), FpsxJson.Options))
            .OfType<SessionRecord>()
            .OrderByDescending(s => s.StartedAt)
            .ToList();

    public void Log(ChangeLogEntry entry)
    {
        lock (_lock)
            File.AppendAllText(_logPath, JsonSerializer.Serialize(entry, FpsxJson.Compact) + Environment.NewLine);
    }
}
