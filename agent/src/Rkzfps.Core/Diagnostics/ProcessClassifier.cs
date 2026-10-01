namespace Rkzfps.Core.Diagnostics;

public enum ProcessCategory
{
    Critical,
    System,
    Game,
    Launcher,
    User,
}

public static class ProcessClassifier
{
    // Processos que o Windows precisa para existir. Aparecem no diagnóstico
    // como "Essencial" e nunca como algo a fechar.
    private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "svchost", "dwm", "fontdrvhost", "Memory Compression", "explorer", "sihost", "ctfmon", "audiodg",
        "MsMpEng", "NisSrv", "SecurityHealthService", "SecurityHealthSystray", "MpDefenderCoreService",
        "spoolsv", "conhost", "RuntimeBroker", "StartMenuExperienceHost", "ShellExperienceHost", "SearchHost",
        "TextInputHost", "dllhost", "WmiPrvSE", "taskhostw", "vgc", "vgk",
    };

    private static readonly HashSet<string> SystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SearchIndexer", "TiWorker", "TrustedInstaller", "MoUsoCoreWorker", "wuauclt", "CompPkgSrv", "SgrmBroker",
        "PhoneExperienceHost", "Widgets", "WidgetService", "backgroundTaskHost", "SystemSettings",
    };

    private static readonly string[] Launchers =
    [
        "steam", "steamwebhelper", "EpicGamesLauncher", "EpicWebHelper", "Battle.net", "EADesktop", "EABackgroundService",
        "UbisoftConnect", "upc", "RiotClientServices", "GalaxyClient", "RockstarService",
    ];

    private static readonly string[] Games =
    [
        "cs2", "VALORANT-Win64-Shipping", "FortniteClient-Win64-Shipping", "League of Legends", "r5apex", "r5apex_dx12", "cod", "cod22-cod",
        "Overwatch", "RainbowSix", "RainbowSix_Vulkan", "RocketLeague", "TslGame", "GTA5", "GTA5_Enhanced", "Minecraft.Windows", "javaw",
        "RobloxPlayerBeta", "dota2", "Marvel-Win64-Shipping", "Minecraft",
    ];

    // Jogos com o ano no nome do executável (FC25, FC27, FIFA23...). Lista
    // fixa por ano quebraria a cada lançamento: o FC27 já foi visto sendo
    // tratado como "programa em segundo plano" e oferecido para fechar.
    private static readonly System.Text.RegularExpressions.Regex YearlyGames =
        new(@"^(FC|FIFA|NBA2K|MADDEN|F1_?)\d{2}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static ProcessCategory Classify(string name)
    {
        if (Critical.Contains(name))
            return ProcessCategory.Critical;
        if (SystemNames.Contains(name))
            return ProcessCategory.System;
        if (Games.Any(g => name.Equals(g, StringComparison.OrdinalIgnoreCase)) || YearlyGames.IsMatch(name))
            return ProcessCategory.Game;
        if (Launchers.Any(l => name.Equals(l, StringComparison.OrdinalIgnoreCase)))
            return ProcessCategory.Launcher;
        return ProcessCategory.User;
    }

    // Programas que ficam em tela cheia e usam a GPU sem ser jogo: vídeo,
    // navegador, streaming, apresentação. Medir um deles viraria "partida" falsa.
    private static readonly HashSet<string> FullscreenNonGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi", "iexplore", "msedgewebview2",
        "vlc", "mpc-hc", "mpc-hc64", "mpc-be64", "PotPlayerMini64", "PotPlayerMini", "wmplayer", "Video.UI", "Microsoft.Media.Player",
        "Netflix", "Spotify", "obs64", "obs32", "Discord", "Teams", "ms-teams", "Zoom", "POWERPNT", "WINWORD", "EXCEL", "Acrobat", "AcroRd32",
        "ApplicationFrameHost", "mstsc", "vmware", "VirtualBoxVM", "Photoshop", "Resolve", "Premiere", "blender", "Code", "devenv",
        "RKZFPS", "Rkzfps.App", "PresentMon",
    };

    /// <summary>
    /// Jogo sem perfil: janela em tela cheia na frente, fora da pasta do
    /// Windows, que não é launcher, sistema nem programa conhecido de tela
    /// cheia, E usando a GPU de verdade. Tela cheia sozinha pega vídeo; GPU
    /// sozinha pega programa 3D em janela. Os dois juntos são um jogo rodando.
    /// </summary>
    public const double MinGame3dPercent = 15;

    public static bool IsLikelyGame(string processName, string? exePath, bool fullscreen, double? gpu3dPercent)
    {
        if (!fullscreen || gpu3dPercent is not >= MinGame3dPercent)
            return false;
        if (Classify(processName) is ProcessCategory.Critical or ProcessCategory.System or ProcessCategory.Launcher)
            return false;
        if (FullscreenNonGames.Contains(processName))
            return false;
        var windows = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        return exePath is null || !exePath.StartsWith(windows, StringComparison.OrdinalIgnoreCase);
    }

    public static string Label(ProcessCategory c) => c switch
    {
        ProcessCategory.Critical => "Essencial",
        ProcessCategory.System => "Sistema",
        ProcessCategory.Game => "Jogo",
        ProcessCategory.Launcher => "Launcher",
        _ => "Software do usuário",
    };
}
