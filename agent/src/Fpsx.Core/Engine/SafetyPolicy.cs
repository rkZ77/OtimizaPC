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
    ];

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
        }
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
    }
}
