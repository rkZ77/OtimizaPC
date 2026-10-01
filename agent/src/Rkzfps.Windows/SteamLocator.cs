using Rkzfps.Core.Games;
using Microsoft.Win32;

namespace Rkzfps.Windows;

public static class SteamLocator
{
    public static string? SteamPath()
    {
        var raw = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string;
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var path = Path.GetFullPath(raw.Replace('/', '\\'));
        return Directory.Exists(path) ? path : null;
    }

    public static IReadOnlyList<string> Libraries()
    {
        var steam = SteamPath();
        if (steam is null)
            return [];

        var libraries = new List<string> { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
            libraries.AddRange(ValveFiles.ParseLibraryFolders(File.ReadAllText(vdf)));
        return libraries.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Biblioteca e pasta de instalação de um app Steam, ou null se não instalado.</summary>
    public static (string Library, string InstallPath)? FindApp(int appId)
    {
        foreach (var library in Libraries())
        {
            var manifest = Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf");
            if (!File.Exists(manifest))
                continue;
            var kv = ValveFiles.ParseFlatKeyValues(File.ReadAllText(manifest));
            if (!kv.TryGetValue("installdir", out var dir) || string.IsNullOrWhiteSpace(dir))
                continue;
            var install = Path.Combine(library, "steamapps", "common", dir);
            if (Directory.Exists(install))
                return (library, install);
        }

        return null;
    }

    /// <summary>Arquivo de config mais recente entre as contas Steam deste PC.</summary>
    public static string? FindUserdataFile(string relativePath)
    {
        var steam = SteamPath();
        var userdata = steam is null ? null : Path.Combine(steam, "userdata");
        if (userdata is null || !Directory.Exists(userdata))
            return null;

        return Directory.EnumerateDirectories(userdata)
            .Select(account => Path.Combine(account, relativePath.Replace('/', '\\')))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
