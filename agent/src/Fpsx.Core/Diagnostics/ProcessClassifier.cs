namespace Fpsx.Core.Diagnostics;

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

    private static readonly string[] Games = ["cs2", "VALORANT-Win64-Shipping", "FortniteClient-Win64-Shipping", "League of Legends", "r5apex", "cod", "Overwatch", "RainbowSix", "RocketLeague", "TslGame", "GTA5", "FC25", "javaw"];

    public static ProcessCategory Classify(string name)
    {
        if (Critical.Contains(name))
            return ProcessCategory.Critical;
        if (SystemNames.Contains(name))
            return ProcessCategory.System;
        if (Games.Any(g => name.Equals(g, StringComparison.OrdinalIgnoreCase)))
            return ProcessCategory.Game;
        if (Launchers.Any(l => name.Equals(l, StringComparison.OrdinalIgnoreCase)))
            return ProcessCategory.Launcher;
        return ProcessCategory.User;
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
