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
    // Um ponto de restauração por sessão, antes do primeiro driver: o Windows
    // recusa pontos seguidos e um só já cobre todos os drivers da sessão.
    private string? _restorePoint;

    public Change? CaptureInverse(Change change) => change switch
    {
        RegistryValueChange r => r with { Value = system.ReadRegistry(r.Root, r.Path, r.Name) },
        PowerSchemeChange => system.GetActivePowerScheme() is { } guid ? new PowerSchemeChange(guid, "plano anterior") : null,
        DisplayRefreshChange d => system.GetDisplayMode(d.DeviceName) is { } m ? new DisplayRefreshChange(d.DeviceName, m.Width, m.Height, m.RefreshHz) : null,
        GameConfigChange g => system.ReadGameConfig(g.GameId, g.Key) is { } prev ? g with { Value = prev } : null,
        AppSettingChange a => system.ReadAppSetting(a.AppId, a.Key) is { } prevApp ? a with { Value = prevApp } : null,
        _ => null,
    };

    public static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(10);

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
            case SystemRepairChange s:
                var repair = system.RunSystemRepair(s.Repair);
                if (repair.ExitCode != 0)
                    throw new InvalidOperationException($"O reparo terminou com código {repair.ExitCode}: {repair.Output.Trim()}");
                return repair.Output.Trim();
            case DriverInstallChange d:
                _restorePoint ??= RestorePoint();
                var install = system.InstallDriverUpdate(d.UpdateId);
                if (install.ExitCode != 0)
                    throw new InvalidOperationException($"O Windows Update não instalou o driver: {install.Output.Trim()}");
                return $"{install.Output.Trim()} {_restorePoint}".Trim();
            case ProcessCloseChange p:
                var current = system.ProcessName(p.Pid);
                if (current is null)
                    return "o programa já tinha fechado";
                // PID é reciclado pelo Windows: se o nome mudou desde o scan, é
                // outro programa, e ele não é tocado.
                if (!string.Equals(current, p.Name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"O PID {p.Pid} agora pertence a {current}. Nada foi fechado.");
                if (!system.CloseProcess(p.Pid, CloseTimeout))
                    throw new InvalidOperationException($"{p.Name} não fechou. Feche manualmente para não perder trabalho não salvo.");
                return "programa fechado";
            case GameConfigChange g:
                // O jogo reescreve o arquivo ao fechar: editar com ele aberto
                // seria desfeito sem ninguém saber.
                if (system.IsGameRunning(g.GameId))
                    throw new InvalidOperationException("Feche o jogo antes de aplicar esta correção.");
                system.WriteGameConfig(g.GameId, g.Key, g.Value);
                return "configuração do jogo gravada";
            case AppSettingChange a:
                // O Discord regrava o settings.json inteiro ao sair: editar com
                // ele aberto (inclusive só na bandeja) seria desfeito em silêncio.
                if (system.IsAppRunning(a.AppId))
                    throw new InvalidOperationException("Feche o Discord antes, inclusive o ícone perto do relógio (botão direito > Sair do Discord).");
                system.WriteAppSetting(a.AppId, a.Key, a.Value);
                return "opção gravada";
            default:
                throw new SafetyViolationException($"Tipo de alteração sem executor: {change.GetType().Name}");
        }
    }

    // Sem ponto (Proteção do Sistema desligada) o driver ainda pode ser
    // revertido no Gerenciador de Dispositivos: instala e diz isso no resultado.
    private string RestorePoint()
    {
        var r = system.CreateRestorePoint("RKZFPS: antes de instalar driver");
        return r.ExitCode == 0
            ? "Ponto de restauração do Windows criado antes."
            : "Sem ponto de restauração (Proteção do Sistema desligada): para voltar, use Reverter driver no Gerenciador de Dispositivos.";
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
        GameConfigChange g => string.Equals(system.ReadGameConfig(g.GameId, g.Key), g.Value, StringComparison.Ordinal)
            ? new Verification(true, $"{g.Key} = {g.Value} confirmado no arquivo")
            : new Verification(false, $"{g.Key} não ficou com o valor {g.Value}"),
        AppSettingChange a => string.Equals(system.ReadAppSetting(a.AppId, a.Key), a.Value, StringComparison.Ordinal)
            ? new Verification(true, $"{a.Key} = {a.Value} confirmado no arquivo")
            : new Verification(false, $"{a.Key} não ficou com o valor {a.Value}"),
        ProcessCloseChange p => !string.Equals(system.ProcessName(p.Pid), p.Name, StringComparison.OrdinalIgnoreCase)
            ? new Verification(true, "processo encerrado")
            : new Verification(false, "o processo continua aberto"),
        // Cache e rede não têm estado para reler: o sucesso é o próprio resultado da execução.
        _ => new Verification(true, "execução concluída"),
    };

    /// <summary>O estado atual ainda é o que o RKZFPS escreveu? Se não, alguém mexeu depois.</summary>
    public bool StillApplied(Change applied) => applied switch
    {
        RegistryValueChange r => Equal(system.ReadRegistry(r.Root, r.Path, r.Name), r.Value),
        PowerSchemeChange p => string.Equals(system.GetActivePowerScheme(), p.SchemeGuid, StringComparison.OrdinalIgnoreCase),
        DisplayRefreshChange d => system.GetDisplayMode(d.DeviceName) is { } m && Math.Abs(m.RefreshHz - d.RefreshHz) <= 1,
        GameConfigChange g => string.Equals(system.ReadGameConfig(g.GameId, g.Key), g.Value, StringComparison.Ordinal),
        AppSettingChange a => string.Equals(system.ReadAppSetting(a.AppId, a.Key), a.Value, StringComparison.Ordinal),
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
