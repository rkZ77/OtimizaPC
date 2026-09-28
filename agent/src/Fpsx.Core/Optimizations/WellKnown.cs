namespace Fpsx.Core.Optimizations;

public static class PowerSchemes
{
    public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string PowerSaver = "a1841308-3541-4fab-bc81-f71556f20b4a";
    public const string UltimatePerformance = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    public static bool Is(string? guid, string known) => string.Equals(guid, known, StringComparison.OrdinalIgnoreCase);
}

public static class RegistryPaths
{
    public const string GameBar = @"Software\Microsoft\GameBar";
    public const string GameDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    public const string GraphicsDrivers = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    public const string StartupApproved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
    public const string MemoryManagement = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";

    /// <summary>Mesma chave que Configurações > Sistema > Tela > Gráficos grava (por usuário).</summary>
    public const string DirectXUserGpuPreferences = @"Software\Microsoft\DirectX\UserGpuPreferences";

    public const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
}

/// <summary>
/// Texto de configuração do DirectX por usuário: pares "Chave=valor;" numa
/// string só. O RKZFPS troca um par e preserva os outros, do mesmo jeito que a
/// tela de Configurações do Windows faz.
/// </summary>
public static class DirectXSettings
{
    public const string GlobalValueName = "DirectXUserGlobalSettings";

    public static Dictionary<string, string> Parse(string? raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (raw ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0)
                result[part[..eq]] = part[(eq + 1)..];
        }

        return result;
    }

    public static string With(string? raw, string key, string value)
    {
        var parts = (raw ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !p.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
            .ToList();
        parts.Add($"{key}={value}");
        return string.Join(";", parts) + ";";
    }
}
