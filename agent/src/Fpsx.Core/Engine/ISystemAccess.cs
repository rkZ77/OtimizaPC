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
}
