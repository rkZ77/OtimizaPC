using Fpsx.Core.Model;

namespace Fpsx.Core.Engine;

public sealed record DisplayMode(int Width, int Height, int RefreshHz);

public sealed record CacheClearResult(int FilesDeleted, long BytesFreed, int FilesSkipped, string Path);

public sealed record CommandResult(int ExitCode, string Output);

/// <summary>
/// Primitivas de leitura e escrita no sistema. É o único ponto do Agent que
/// toca Windows; os testes trocam por uma implementação em memória.
/// </summary>
public interface ISystemAccess
{
    RegValue? ReadRegistry(RegistryRoot root, string path, string name);

    void WriteRegistry(RegistryRoot root, string path, string name, RegValue value);

    void DeleteRegistryValue(RegistryRoot root, string path, string name);

    string? GetActivePowerScheme();

    void SetActivePowerScheme(string guid);

    DisplayMode? GetDisplayMode(string deviceName);

    void SetDisplayMode(string deviceName, DisplayMode mode);

    CacheClearResult ClearCache(CacheTarget target);

    CommandResult RunNetworkRepair(NetworkRepairKind kind);

    CommandResult RunSystemRepair(SystemRepairKind kind);

    /// <summary>Ponto de restauração do Windows. Falha quando a Proteção do Sistema está desligada.</summary>
    CommandResult CreateRestorePoint(string description);

    /// <summary>Baixa e instala pelo Windows Update a atualização de driver com esse id.</summary>
    CommandResult InstallDriverUpdate(string updateId);

    /// <summary>Nome do processo com esse PID agora, ou null se ele já terminou.</summary>
    string? ProcessName(int pid);

    /// <summary>Pede o fechamento gracioso. Retorna true se o processo terminou dentro do prazo.</summary>
    bool CloseProcess(int pid, TimeSpan timeout);

    bool IsGameRunning(string gameId);

    string? ReadGameConfig(string gameId, string key);

    void WriteGameConfig(string gameId, string key, string value);

    bool IsAppRunning(string appId);

    /// <summary>Valor atual da opção, ou null quando o programa não está instalado.</summary>
    string? ReadAppSetting(string appId, string key);

    void WriteAppSetting(string appId, string key, string value);
}
