using System.Text.RegularExpressions;
using Fpsx.Core.Model;
using Fpsx.Core.Optimizations;

namespace Fpsx.Core.Engine;

public sealed class SafetyViolationException(string message) : Exception(message);

/// <summary>
/// Última barreira antes de qualquer escrita: whitelist de chaves de registro
/// e limites de valor. Vale também para o rollback. Um handler com bug ou um
/// catálogo adulterado não consegue escrever fora daqui.
/// </summary>
public static partial class SafetyPolicy
{
    private sealed record RegistryRule(RegistryRoot Root, string Path, bool AnySubkeyName, string[]? Names);

    private static readonly RegistryRule[] AllowedRegistry =
    [
        new(RegistryRoot.CurrentUser, RegistryPaths.GameBar, false, ["AutoGameModeEnabled"]),
        new(RegistryRoot.CurrentUser, RegistryPaths.GameDvr, false, ["HistoricalCaptureEnabled", "AppCaptureEnabled"]),
        new(RegistryRoot.LocalMachine, RegistryPaths.GraphicsDrivers, false, ["HwSchMode"]),
        new(RegistryRoot.CurrentUser, RegistryPaths.StartupApproved + @"\Run", false, null),
        new(RegistryRoot.CurrentUser, RegistryPaths.StartupApproved + @"\StartupFolder", false, null),
        new(RegistryRoot.LocalMachine, RegistryPaths.StartupApproved + @"\Run", false, null),
        new(RegistryRoot.LocalMachine, RegistryPaths.StartupApproved + @"\Run32", false, null),
        new(RegistryRoot.LocalMachine, RegistryPaths.StartupApproved + @"\StartupFolder", false, null),
        new(RegistryRoot.LocalMachine, RegistryPaths.MemoryManagement, false, ["PagingFiles"]),
    ];

    // Chaves de configuração de jogo que o FPSX pode escrever, com o formato
    // aceito. Fica compilado aqui, e não no perfil JSON: um perfil adulterado
    // não pode transformar o FPSX num editor de config arbitrário.
    private static readonly Dictionary<string, Dictionary<string, Regex>> AllowedGameKeys = new()
    {
        ["cs2"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["setting.mat_vsync"] = new("^[01]$"),
            ["setting.r_low_latency"] = new("^[012]$"),
            ["setting.refreshrate_numerator"] = new("^[1-9][0-9]{1,6}$"),
            ["setting.refreshrate_denominator"] = new("^[1-9][0-9]{0,3}$"),
        },
    };

    [GeneratedRegex(@"^[A-Za-z?]:\\pagefile\.sys( \d+ \d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex PagingFileLine();

    // Defesa em profundidade: mesmo que alguém amplie a whitelist por engano,
    // nada que toque segurança, atualização ou políticas passa.
    private static readonly string[] ForbiddenFragments =
    [
        "windows defender", "windowsupdate", "sharedaccess", @"\mpssvc\", @"\policies\", @"\lsa\", "securityhealth", @"\wscsvc\", @"\bfe\",
    ];

    [GeneratedRegex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex GuidPattern();

    public static void Validate(Change change)
    {
        switch (change)
        {
            case RegistryValueChange r:
                ValidateRegistry(r);
                break;
            case PowerSchemeChange p when !GuidPattern().IsMatch(p.SchemeGuid):
                throw new SafetyViolationException($"GUID de plano de energia inválido: {p.SchemeGuid}");
            case DisplayRefreshChange d when d.RefreshHz is < 24 or > 1000 || d.Width <= 0 || d.Height <= 0 || !d.DeviceName.StartsWith(@"\\.\DISPLAY", StringComparison.OrdinalIgnoreCase):
                throw new SafetyViolationException($"Modo de vídeo fora dos limites: {d.Describe()}");
            case CacheClearChange c when !Enum.IsDefined(c.Target):
            case NetworkRepairChange n when !Enum.IsDefined(n.Repair):
                throw new SafetyViolationException("Operação desconhecida.");
            case ProcessCloseChange p:
                ValidateProcessClose(p);
                break;
            case GameConfigChange g:
                if (!AllowedGameKeys.TryGetValue(g.GameId, out var keys) || !keys.TryGetValue(g.Key, out var format))
                    throw new SafetyViolationException($"Chave de configuração fora da whitelist: {g.GameId}/{g.Key}");
                if (!format.IsMatch(g.Value))
                    throw new SafetyViolationException($"Valor fora do formato permitido para {g.Key}: {g.Value}");
                break;
        }
    }

    /// <summary>
    /// Nunca fecha processo do Windows, de segurança, de anti-cheat, launcher
    /// (o CS2 depende da Steam aberta), jogo ou o próprio FPSX.
    /// </summary>
    private static void ValidateProcessClose(ProcessCloseChange p)
    {
        if (p.Pid <= 4 || string.IsNullOrWhiteSpace(p.Name))
            throw new SafetyViolationException("Processo inválido.");
        var category = Diagnostics.ProcessClassifier.Classify(p.Name);
        if (category is not Diagnostics.ProcessCategory.User)
            throw new SafetyViolationException($"{p.Name} é {Diagnostics.ProcessClassifier.Label(category).ToLowerInvariant()} e não pode ser fechado pelo FPSX.");
        var startup = StartupClassifier.Classify(new StartupEntry { Name = p.Name });
        if (StartupClassifier.IsProtected(startup))
            throw new SafetyViolationException($"{p.Name} é protegido e não pode ser fechado pelo FPSX.");
        if (p.Name.StartsWith("fpsx", StringComparison.OrdinalIgnoreCase))
            throw new SafetyViolationException("O FPSX não fecha a si mesmo.");
    }

    private static void ValidateRegistry(RegistryValueChange r)
    {
        var path = r.Path.Trim('\\');
        // Só o caminho: o nome do valor em StartupApproved é o nome do programa,
        // e esse caso é tratado pelo classificador logo abaixo.
        var lowered = @"\" + path.ToLowerInvariant() + @"\";
        if (ForbiddenFragments.Any(f => lowered.Contains(f)))
            throw new SafetyViolationException($"Chave protegida: {r.FullPath}\\{r.Name}");

        var rule = AllowedRegistry.FirstOrDefault(a => a.Root == r.Root && string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase));
        if (rule is null)
            throw new SafetyViolationException($"Chave fora da whitelist: {r.FullPath}");
        if (rule.Names is not null && !rule.Names.Contains(r.Name, StringComparer.OrdinalIgnoreCase))
            throw new SafetyViolationException($"Valor fora da whitelist: {r.FullPath}\\{r.Name}");

        // Nas chaves de StartupApproved o nome do valor é o programa: segurança
        // e anti-cheat não podem ser desligados nem por pedido explícito.
        if (path.StartsWith(RegistryPaths.StartupApproved, StringComparison.OrdinalIgnoreCase) && r.Value is not null)
        {
            var category = StartupClassifier.Classify(new StartupEntry { Name = r.Name });
            if (StartupClassifier.IsProtected(category) && !StartupOptimization.IsEnabledRaw(r.Value.Data))
                throw new SafetyViolationException($"Item de inicialização protegido: {r.Name}");
            if (r.Value.Kind != RegistryKind.Binary)
                throw new SafetyViolationException("StartupApproved só aceita valor binário.");
        }

        // PagingFiles só aceita linhas no formato do próprio Windows
        // ("?:\pagefile.sys" = gerenciado pelo sistema). Vazio é permitido só
        // para o rollback devolver o estado "sem pagefile" que o usuário tinha.
        if (string.Equals(path, RegistryPaths.MemoryManagement, StringComparison.OrdinalIgnoreCase))
        {
            if (r.Value is null || r.Value.Kind != RegistryKind.MultiString)
                throw new SafetyViolationException("PagingFiles precisa ser REG_MULTI_SZ e não pode ser apagado.");
            var lines = r.Value.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Any(l => !PagingFileLine().IsMatch(l.Trim())))
                throw new SafetyViolationException($"Valor de PagingFiles fora do formato permitido: {r.Value.Data}");
        }
    }
}
