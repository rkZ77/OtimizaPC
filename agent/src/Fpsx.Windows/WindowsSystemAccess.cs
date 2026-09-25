using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Fpsx.Core.Engine;
using Fpsx.Core.Games;
using Fpsx.Core.Model;
using Microsoft.Win32;

namespace Fpsx.Windows;

/// <summary>Implementação real das primitivas. Toda escrita passa antes pela SafetyPolicy no ChangeExecutor.</summary>
public sealed class WindowsSystemAccess(IReadOnlyList<GameProfile>? gameProfiles = null) : ISystemAccess
{
    private readonly IReadOnlyList<GameProfile> _games = gameProfiles ?? [];

    private static RegistryKey Hive(RegistryRoot root) => root == RegistryRoot.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine;

    public RegValue? ReadRegistry(RegistryRoot root, string path, string name)
    {
        using var key = Hive(root).OpenSubKey(path);
        if (key is null || !key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
            return null;

        var kind = key.GetValueKind(name);
        var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return kind switch
        {
            RegistryValueKind.DWord => new RegValue(RegistryKind.DWord, Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)),
            RegistryValueKind.QWord => new RegValue(RegistryKind.QWord, Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)),
            RegistryValueKind.Binary => new RegValue(RegistryKind.Binary, Convert.ToHexString((byte[])value!)),
            RegistryValueKind.String or RegistryValueKind.ExpandString => new RegValue(RegistryKind.String, value?.ToString() ?? ""),
            RegistryValueKind.MultiString => new RegValue(RegistryKind.MultiString, string.Join('\n', (string[])value!)),
            // Tipo que o FPSX não sabe restaurar fielmente: tratar como
            // não-lido faz o motor recusar, em vez de sobrescrever às cegas.
            _ => throw new NotSupportedException($"Tipo de registro não suportado em {path}\\{name}: {kind}"),
        };
    }

    public void WriteRegistry(RegistryRoot root, string path, string name, RegValue value)
    {
        using var key = Hive(root).CreateSubKey(path, writable: true);
        switch (value.Kind)
        {
            case RegistryKind.DWord:
                key.SetValue(name, int.Parse(value.Data, CultureInfo.InvariantCulture), RegistryValueKind.DWord);
                break;
            case RegistryKind.QWord:
                key.SetValue(name, long.Parse(value.Data, CultureInfo.InvariantCulture), RegistryValueKind.QWord);
                break;
            case RegistryKind.Binary:
                key.SetValue(name, Convert.FromHexString(value.Data), RegistryValueKind.Binary);
                break;
            case RegistryKind.String:
                key.SetValue(name, value.Data, RegistryValueKind.String);
                break;
            case RegistryKind.MultiString:
                key.SetValue(name, value.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries), RegistryValueKind.MultiString);
                break;
        }
    }

    // ---- processos ----

    public string? ProcessName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.HasExited ? null : p.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public bool CloseProcess(int pid, TimeSpan timeout)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            // Só o pedido educado (WM_CLOSE na janela principal). Processo sem
            // janela não recebe pedido nenhum e o FPSX informa que não fechou:
            // matar à força pode corromper arquivo ou perder trabalho.
            if (!p.CloseMainWindow())
                return p.HasExited;
            return p.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    // ---- configuração de jogo ----

    public bool IsGameRunning(string gameId) =>
        Profile(gameId).Detect.Processes
            .Select(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Any(name => Process.GetProcessesByName(name).Length > 0);

    public string? ReadGameConfig(string gameId, string key)
    {
        var path = GameConfigPath(gameId);
        return path is null ? null : Fpsx.Core.Games.ValveFiles.ParseFlatKeyValues(File.ReadAllText(path)).GetValueOrDefault(key);
    }

    public void WriteGameConfig(string gameId, string key, string value)
    {
        var path = GameConfigPath(gameId) ?? throw new InvalidOperationException("Arquivo de configuração do jogo não encontrado.");
        var text = File.ReadAllText(path);
        var updated = Fpsx.Core.Games.ValveFiles.ReplaceValue(text, key, value)
                      ?? throw new InvalidOperationException($"A chave {key} não existe no arquivo do jogo.");

        // Cópia do arquivo original, uma vez só, além do inverso por chave
        // que o motor já guarda: se algo sair errado, o usuário tem o arquivo inteiro.
        var backup = path + ".fpsx-original";
        if (!File.Exists(backup))
            File.Copy(path, backup);

        var tmp = path + ".fpsx-tmp";
        File.WriteAllText(tmp, updated);
        File.Move(tmp, path, overwrite: true);
    }

    private GameProfile Profile(string gameId) =>
        _games.FirstOrDefault(g => g.Id == gameId) ?? throw new InvalidOperationException($"Perfil de jogo desconhecido: {gameId}");

    private string? GameConfigPath(string gameId)
    {
        var source = Profile(gameId).Config;
        return source is { Source: "steam_userdata", Format: "valve_kv" } ? SteamLocator.FindUserdataFile(source.RelativePath) : null;
    }

    public void DeleteRegistryValue(RegistryRoot root, string path, string name)
    {
        using var key = Hive(root).OpenSubKey(path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public string? GetActivePowerScheme() => PowerApi.ActiveScheme();

    public void SetActivePowerScheme(string guid)
    {
        var g = Guid.Parse(guid);
        var rc = Native.PowerSetActiveScheme(IntPtr.Zero, ref g);
        if (rc != 0)
            throw new InvalidOperationException($"PowerSetActiveScheme falhou com código {rc}.");
    }

    public DisplayMode? GetDisplayMode(string deviceName) => DisplayApi.Current(deviceName);

    public void SetDisplayMode(string deviceName, DisplayMode mode) => DisplayApi.Apply(deviceName, mode);

    public CacheClearResult ClearCache(CacheTarget target)
    {
        var dirs = CacheDirectories(target).Where(Directory.Exists).ToList();
        int deleted = 0, skipped = 0;
        long bytes = 0;
        // Temporário recente pode ser de um instalador ou programa rodando agora.
        var cutoff = target == CacheTarget.UserTemp ? DateTime.UtcNow.AddHours(-24) : DateTime.MaxValue;
        foreach (var dir in dirs)
            ClearDirectory(new DirectoryInfo(dir), cutoff, ref deleted, ref skipped, ref bytes);
        return new CacheClearResult(deleted, bytes, skipped, dirs.Count == 0 ? "(nenhuma pasta de cache encontrada)" : string.Join("; ", dirs));
    }

    // Os caminhos são decididos aqui, por alvo, e nunca recebidos de fora.
    private static IEnumerable<string> CacheDirectories(CacheTarget target)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var localLow = Path.Combine(Path.GetDirectoryName(local)!, "LocalLow");
        switch (target)
        {
            case CacheTarget.DirectXShaderCache:
                yield return Path.Combine(local, "D3DSCache");
                break;
            case CacheTarget.NvidiaDxCache:
                yield return Path.Combine(local, "NVIDIA", "DXCache");
                yield return Path.Combine(localLow, "NVIDIA", "PerDriverVersion", "DXCache");
                break;
            case CacheTarget.AmdDxCache:
                yield return Path.Combine(local, "AMD", "DxCache");
                yield return Path.Combine(local, "AMD", "DxcCache");
                break;
            case CacheTarget.SteamShaderCacheCs2:
                if (SteamLocator.FindApp(730) is { } cs2)
                    yield return Path.Combine(cs2.Library, "steamapps", "shadercache", "730");
                break;
            case CacheTarget.UserTemp:
                yield return Path.GetTempPath();
                break;
        }
    }

    private static void ClearDirectory(DirectoryInfo dir, DateTime cutoffUtc, ref int deleted, ref int skipped, ref long bytes)
    {
        foreach (var file in dir.EnumerateFiles())
        {
            if (file.LastWriteTimeUtc > cutoffUtc)
                continue;
            try
            {
                var size = file.Length;
                file.Delete();
                deleted++;
                bytes += size;
            }
            catch (IOException)
            {
                skipped++;
            }
            catch (UnauthorizedAccessException)
            {
                skipped++;
            }
        }

        foreach (var sub in dir.EnumerateDirectories())
        {
            // Junction/symlink dentro de cache não é seguido: apagar através
            // de um link poderia atingir uma pasta fora do cache.
            if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                skipped++;
                continue;
            }

            ClearDirectory(sub, cutoffUtc, ref deleted, ref skipped, ref bytes);
            try
            {
                sub.Delete(recursive: false);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public CommandResult RunNetworkRepair(NetworkRepairKind kind)
    {
        var (exe, args) = kind switch
        {
            NetworkRepairKind.FlushDns => ("ipconfig.exe", new[] { "/flushdns" }),
            NetworkRepairKind.WinsockReset => ("netsh.exe", new[] { "winsock", "reset" }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        // Caminho absoluto do System32: um ipconfig.exe plantado na pasta atual
        // ou no PATH nunca é o executado.
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, exe))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Não foi possível iniciar {exe}.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        if (!process.WaitForExit(60_000))
        {
            process.Kill();
            return new CommandResult(-1, "Tempo esgotado.");
        }

        return new CommandResult(process.ExitCode, output);
    }
}

internal static class PowerApi
{
    public static string? ActiveScheme()
    {
        if (Native.PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0 || ptr == IntPtr.Zero)
            return null;
        try
        {
            return Marshal.PtrToStructure<Guid>(ptr).ToString();
        }
        finally
        {
            Native.LocalFree(ptr);
        }
    }

    public static List<PowerScheme> Schemes()
    {
        var result = new List<PowerScheme>();
        for (uint i = 0; ; i++)
        {
            var size = 16u;
            var buffer = new byte[16];
            if (Native.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, Native.AccessScheme, i, buffer, ref size) != 0)
                break;
            var guid = new Guid(buffer);
            result.Add(new PowerScheme(guid.ToString(), FriendlyName(guid) ?? guid.ToString()));
        }

        return result;
    }

    private static string? FriendlyName(Guid guid)
    {
        uint size = 0;
        if (Native.PowerReadFriendlyName(IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, null, ref size) != 0 || size == 0)
            return null;
        var buffer = new byte[size];
        if (Native.PowerReadFriendlyName(IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, buffer, ref size) != 0)
            return null;
        return System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }
}

internal static class DisplayApi
{
    public static IEnumerable<(string Device, string Friendly, bool Primary)> Devices()
    {
        for (uint i = 0; ; i++)
        {
            var dd = new Native.DisplayDevice { Cb = Marshal.SizeOf<Native.DisplayDevice>() };
            if (!Native.EnumDisplayDevices(null, i, ref dd, 0))
                yield break;
            if ((dd.StateFlags & Native.DisplayAttachedToDesktop) == 0)
                continue;

            var monitor = new Native.DisplayDevice { Cb = Marshal.SizeOf<Native.DisplayDevice>() };
            var friendly = Native.EnumDisplayDevices(dd.DeviceName, 0, ref monitor, 0) ? monitor.DeviceString : dd.DeviceString;
            yield return (dd.DeviceName, friendly, (dd.StateFlags & Native.DisplayPrimary) != 0);
        }
    }

    public static DisplayMode? Current(string device)
    {
        var dm = Native.DevMode.Create();
        return Native.EnumDisplaySettings(device, Native.EnumCurrentSettings, ref dm)
            ? new DisplayMode((int)dm.PelsWidth, (int)dm.PelsHeight, (int)dm.DisplayFrequency)
            : null;
    }

    /// <summary>Maior taxa que o driver lista para a resolução e profundidade de cor atuais.</summary>
    public static int MaxHz(string device, int width, int height)
    {
        var current = Native.DevMode.Create();
        var bpp = Native.EnumDisplaySettings(device, Native.EnumCurrentSettings, ref current) ? current.BitsPerPel : 32;
        var max = 0;
        for (var i = 0; ; i++)
        {
            var dm = Native.DevMode.Create();
            if (!Native.EnumDisplaySettings(device, i, ref dm))
                break;
            if (dm.PelsWidth == width && dm.PelsHeight == height && dm.BitsPerPel == bpp)
                max = Math.Max(max, (int)dm.DisplayFrequency);
        }

        return max;
    }

    public static void Apply(string device, DisplayMode mode)
    {
        var dm = Native.DevMode.Create();
        if (!Native.EnumDisplaySettings(device, Native.EnumCurrentSettings, ref dm))
            throw new InvalidOperationException($"Não foi possível ler o modo atual de {device}.");

        dm.PelsWidth = (uint)mode.Width;
        dm.PelsHeight = (uint)mode.Height;
        dm.DisplayFrequency = (uint)mode.RefreshHz;
        dm.Fields = Native.DmPelsWidth | Native.DmPelsHeight | Native.DmDisplayFrequency;

        // CDS_TEST primeiro: o driver confirma que o modo é válido antes de a
        // tela piscar.
        var test = Native.ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, Native.CdsTest, IntPtr.Zero);
        if (test != Native.DispChangeSuccessful)
            throw new InvalidOperationException($"O driver recusou {mode.Width}x{mode.Height} a {mode.RefreshHz} Hz (código {test}).");

        var rc = Native.ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, Native.CdsUpdateRegistry, IntPtr.Zero);
        if (rc != Native.DispChangeSuccessful)
            throw new InvalidOperationException($"Falha ao aplicar o modo de vídeo (código {rc}).");
    }
}
