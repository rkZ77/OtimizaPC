using Fpsx.Core.Model;

namespace Fpsx.Core.Engine;

public sealed record Verification(bool Passed, string Detail);

/// <summary>
/// Captura o estado anterior como uma alteração inversa, aplica e verifica
/// lendo de volta. O inverso é calculado ANTES de aplicar e gravado em disco
/// antes da escrita: se o Agent cair no meio, o backup já existe.
/// </summary>
public sealed class ChangeExecutor(ISystemAccess system)
{
    public Change? CaptureInverse(Change change) => change switch
    {
        RegistryValueChange r => r with { Value = system.ReadRegistry(r.Root, r.Path, r.Name) },
        PowerSchemeChange => system.GetActivePowerScheme() is { } guid ? new PowerSchemeChange(guid, "plano anterior") : null,
        DisplayRefreshChange d => system.GetDisplayMode(d.DeviceName) is { } m ? new DisplayRefreshChange(d.DeviceName, m.Width, m.Height, m.RefreshHz) : null,
        _ => null,
    };

    public string Apply(Change change)
    {
        SafetyPolicy.Validate(change);
        switch (change)
        {
            case RegistryValueChange { Value: null } r:
                system.DeleteRegistryValue(r.Root, r.Path, r.Name);
                return "valor removido";
            case RegistryValueChange r:
                system.WriteRegistry(r.Root, r.Path, r.Name, r.Value);
                return "valor gravado";
            case PowerSchemeChange p:
                system.SetActivePowerScheme(p.SchemeGuid);
                return "plano ativado";
            case DisplayRefreshChange d:
                system.SetDisplayMode(d.DeviceName, new DisplayMode(d.Width, d.Height, d.RefreshHz));
                return "modo de vídeo aplicado";
            case CacheClearChange c:
                var cache = system.ClearCache(c.Target);
                return $"{cache.FilesDeleted} arquivos removidos ({cache.BytesFreed / 1024 / 1024} MB), {cache.FilesSkipped} em uso mantidos, em {cache.Path}";
            case NetworkRepairChange n:
                var result = system.RunNetworkRepair(n.Repair);
                if (result.ExitCode != 0)
                    throw new InvalidOperationException($"Comando terminou com código {result.ExitCode}: {result.Output.Trim()}");
                return result.Output.Trim();
            default:
                throw new SafetyViolationException($"Tipo de alteração sem executor: {change.GetType().Name}");
        }
    }

    public Verification Verify(Change change) => change switch
    {
        RegistryValueChange r => VerifyRegistry(r),
        PowerSchemeChange p => string.Equals(system.GetActivePowerScheme(), p.SchemeGuid, StringComparison.OrdinalIgnoreCase)
            ? new Verification(true, "plano ativo confirmado")
            : new Verification(false, $"plano ativo continua {system.GetActivePowerScheme()}"),
        DisplayRefreshChange d => system.GetDisplayMode(d.DeviceName) is { } m && Math.Abs(m.RefreshHz - d.RefreshHz) <= 1
            ? new Verification(true, $"monitor em {d.RefreshHz} Hz")
            : new Verification(false, "o monitor não ficou na taxa pedida"),
        // Cache e rede não têm estado para reler: o sucesso é o próprio resultado da execução.
        _ => new Verification(true, "execução concluída"),
    };

    /// <summary>O estado atual ainda é o que o FPSX escreveu? Se não, alguém mexeu depois.</summary>
    public bool StillApplied(Change applied) => applied switch
    {
        RegistryValueChange r => Equal(system.ReadRegistry(r.Root, r.Path, r.Name), r.Value),
        PowerSchemeChange p => string.Equals(system.GetActivePowerScheme(), p.SchemeGuid, StringComparison.OrdinalIgnoreCase),
        DisplayRefreshChange d => system.GetDisplayMode(d.DeviceName) is { } m && Math.Abs(m.RefreshHz - d.RefreshHz) <= 1,
        _ => true,
    };

    private Verification VerifyRegistry(RegistryValueChange r)
    {
        var now = system.ReadRegistry(r.Root, r.Path, r.Name);
        return Equal(now, r.Value)
            ? new Verification(true, $"lido de volta: {now?.Data ?? "ausente"}")
            : new Verification(false, $"esperado {r.Value?.Data ?? "ausente"}, lido {now?.Data ?? "ausente"}");
    }

    private static bool Equal(RegValue? a, RegValue? b)
    {
        if (a is null || b is null)
            return a is null && b is null;
        return a.Kind == b.Kind && string.Equals(a.Data, b.Data, StringComparison.OrdinalIgnoreCase);
    }
}
