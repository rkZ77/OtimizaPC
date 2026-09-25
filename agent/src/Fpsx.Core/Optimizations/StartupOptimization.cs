using Fpsx.Core.Model;

namespace Fpsx.Core.Optimizations;

public enum StartupCategory
{
    Security,
    Hardware,
    AntiCheat,
    Launcher,
    Communication,
    CloudSync,
    Media,
    Other,
}

public static class StartupClassifier
{
    // Casamento por substring no nome do valor ou no comando. A lista só
    // precisa acertar os PROTEGIDOS com folga: errar um launcher para "Other"
    // não causa dano, porque nada aqui é desligado sem o usuário escolher.
    private static readonly (StartupCategory Category, string[] Needles)[] Rules =
    [
        (StartupCategory.Security, ["securityhealth", "windowsdefender", "msmpeng", "avast", "avg", "kaspersky", "bitdefender", "norton", "mcafee", "eset", "malwarebytes", "sophos", "trendmicro"]),
        (StartupCategory.AntiCheat, ["vgtray", "vanguard", "faceit", "easyanticheat", "battleye", "esea"]),
        (StartupCategory.Hardware, ["rtkaud", "realtek", "nvidia", "nvcontainer", "radeon", "amdsoftware", "amd ", "igfx", "intel", "synaptics", "elan", "wacom", "dolby", "nahimic", "waves", "logitech", "lghub", "razer", "corsair", "icue", "steelseries", "hyperx", "msi ", "asus", "armoury", "lenovo", "vantage", "dell", "hp ", "bluetooth"]),
        (StartupCategory.Launcher, ["steam", "epicgames", "epic games", "battle.net", "eadesktop", "ea app", "origin", "ubisoft", "upc.exe", "riotclient", "gog galaxy", "rockstar", "xboxapp"]),
        (StartupCategory.Communication, ["discord", "teams", "skype", "zoom", "whatsapp", "telegram", "slack"]),
        (StartupCategory.CloudSync, ["onedrive", "dropbox", "googledrive", "google drive", "icloud", "mega"]),
        (StartupCategory.Media, ["spotify", "itunes", "vlc", "obs"]),
    ];

    public static StartupCategory Classify(StartupEntry e)
    {
        var haystack = (e.Name + " " + e.Command).ToLowerInvariant();
        foreach (var (category, needles) in Rules)
            if (needles.Any(haystack.Contains))
                return category;
        return StartupCategory.Other;
    }

    /// <summary>Segurança, drivers e anti-cheat nunca entram na lista de desligar.</summary>
    public static bool IsProtected(StartupCategory c) =>
        c is StartupCategory.Security or StartupCategory.Hardware or StartupCategory.AntiCheat;

    public static string Label(StartupCategory c) => c switch
    {
        StartupCategory.Security => "Segurança",
        StartupCategory.Hardware => "Driver/hardware",
        StartupCategory.AntiCheat => "Anti-cheat",
        StartupCategory.Launcher => "Launcher",
        StartupCategory.Communication => "Comunicação",
        StartupCategory.CloudSync => "Sincronização",
        StartupCategory.Media => "Mídia",
        _ => "Software do usuário",
    };
}

/// <summary>
/// Desliga item de inicialização do mesmo jeito que o Gerenciador de Tarefas:
/// pelo StartupApproved, sem apagar a entrada do Run. O programa continua
/// instalado e o usuário reativa pelo próprio Windows ou pelo rollback.
/// </summary>
public sealed class StartupOptimization : IOptimization
{
    public string Id => "startup-entry-disable";

    public Evaluation Evaluate(EvaluationContext context)
    {
        var s = context.Snapshot;
        var enabled = s.Startup.Where(e => e.Enabled).ToList();
        var evidence = new Dictionary<string, string>();
        var proposals = new List<Proposal>();

        foreach (var e in enabled)
        {
            var category = StartupClassifier.Classify(e);
            var ram = e.RunningWorkingSetBytes is { } b ? $"{b / (1024 * 1024)} MB em uso agora" : "não está rodando agora";
            evidence[e.Name] = $"{StartupClassifier.Label(category)}, {ram}";

            if (StartupClassifier.IsProtected(category))
                continue;
            if (ApprovedLocation(e.Location) is not { } location)
                continue;

            proposals.Add(new Proposal(
                $"{Id}:{e.Name}",
                $"Não iniciar \"{e.Name}\" com o Windows ({StartupClassifier.Label(category)})",
                [new RegistryValueChange(location.Root, location.Path, e.Name, DisabledValue(s.CapturedAt))],
                Potential.Low,
                $"Libera memória e tempo de boot. {ram}. O programa continua instalado e abre normalmente quando você quiser."));
        }

        if (enabled.Count == 0)
            return Evaluation.Optimal("Nenhum programa de terceiros inicia com o Windows.", evidence);
        if (proposals.Count == 0)
            return Evaluation.Optimal("Só itens de segurança, drivers ou anti-cheat iniciam com o Windows. Nenhuma alteração recomendada.", evidence);

        return new Evaluation
        {
            Decision = Decision.Optional,
            Potential = Potential.Low,
            Reason = $"{enabled.Count} programa(s) iniciam com o Windows. Você escolhe o que manter: o FPSX nunca desliga item de inicialização sozinho.",
            Evidence = evidence,
            Proposals = proposals,
        };
    }

    public static (RegistryRoot Root, string Path)? ApprovedLocation(string location) => location switch
    {
        "HKCU_RUN" => (RegistryRoot.CurrentUser, RegistryPaths.StartupApproved + @"\Run"),
        "HKLM_RUN" => (RegistryRoot.LocalMachine, RegistryPaths.StartupApproved + @"\Run"),
        "HKLM_RUN32" => (RegistryRoot.LocalMachine, RegistryPaths.StartupApproved + @"\Run32"),
        "STARTUP_FOLDER_USER" => (RegistryRoot.CurrentUser, RegistryPaths.StartupApproved + @"\StartupFolder"),
        "STARTUP_FOLDER_COMMON" => (RegistryRoot.LocalMachine, RegistryPaths.StartupApproved + @"\StartupFolder"),
        _ => null,
    };

    /// <summary>
    /// Formato do Gerenciador de Tarefas: 03 00 00 00 + FILETIME de quando foi
    /// desligado. Byte inicial ímpar = desativado.
    /// </summary>
    public static RegValue DisabledValue(DateTimeOffset at)
    {
        var bytes = new byte[12];
        bytes[0] = 0x03;
        BitConverter.GetBytes(at.UtcDateTime.ToFileTimeUtc()).CopyTo(bytes, 4);
        return new RegValue(RegistryKind.Binary, Convert.ToHexString(bytes));
    }

    public static bool IsEnabledRaw(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length < 2)
            return true;
        return (Convert.FromHexString(hex[..2])[0] & 0x01) == 0;
    }
}
