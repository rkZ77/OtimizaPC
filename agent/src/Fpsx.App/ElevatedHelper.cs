using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Fpsx.Client;
using Fpsx.Core.Engine;
using Fpsx.Core.Json;
using Fpsx.Windows;

namespace Fpsx.App;

/// <summary>Pedido para o processo elevado: aplicar propostas ou desfazer.</summary>
public sealed record ElevatedRequest
{
    public string Action { get; init; } = "";
    public IReadOnlyList<string> Ids { get; init; } = [];
    public bool Experimental { get; init; }
    public string? SessionId { get; init; }
    public string? ChangeId { get; init; }
    public bool Force { get; init; }

    /// <summary>Pasta escolhida pela pessoa: destino do kit (exportar) ou kit a reinstalar.</summary>
    public string? Folder { get; init; }
}

/// <summary>
/// O RKZFPS roda sem administrador. Quando o usuário aplica algo que mexe no
/// sistema (HKLM, pagefile, HAGS, Winsock), o app abre uma cópia de si mesmo
/// com a permissão do Windows (UAC) só para aquilo, e ela fecha em seguida.
///
/// Só vão para cá as propostas que EXIGEM administrador: se o UAC for
/// confirmado com a senha de outra conta, o processo elevado roda como essa
/// outra pessoa, e uma alteração "do usuário" iria para a conta errada.
/// </summary>
public static class ElevatedHelper
{
    public const string Flag = "--elevated";

    /// <summary>Lado do app comum: grava o pedido, pede o UAC e espera. null = usuário recusou.</summary>
    public static async Task<string?> RunAsync(AgentContext ctx, ElevatedRequest request)
    {
        var dir = Path.Combine(ctx.DataDir, "elevated");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(request, FpsxJson.Options));
        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            psi.ArgumentList.Add(Flag);
            psi.ArgumentList.Add(path);
            psi.ArgumentList.Add("--data-dir");
            psi.ArgumentList.Add(ctx.DataDir);
            using var p = Process.Start(psi);
            if (p is null)
                return null;
            await p.WaitForExitAsync();
            var result = path + ".result";
            return File.Exists(result) ? (await File.ReadAllTextAsync(result)).Trim() : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null; // UAC recusado
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + ".result");
        }
    }

    /// <summary>Lado elevado: executa o pedido e devolve o id da sessão. Sem janela principal.</summary>
    public static int Execute(string requestPath)
    {
        var ctx = AppHost.Current.Ctx;
        // O pedido só vale se estiver na pasta de dados do RKZFPS: um arquivo
        // qualquer passado na linha de comando não vira alteração elevada.
        var full = Path.GetFullPath(requestPath);
        if (!full.StartsWith(Path.Combine(Path.GetFullPath(ctx.DataDir), "elevated") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return 2;
        var request = JsonSerializer.Deserialize<ElevatedRequest>(File.ReadAllText(full), FpsxJson.Options);
        if (request is null)
            return 2;

        var system = new WindowsSystemAccess(ctx.GameProfiles);
        SessionRecord session;
        if (request.Action == "rollback" && request.SessionId is { } sid)
        {
            var manager = new RollbackManager(system, ctx.Store);
            session = request.ChangeId is null ? manager.RollbackSession(sid, request.Force) : manager.RollbackChange(sid, request.ChangeId, request.Force);
        }
        else if (request.Action == "export-drivers" && request.Folder is { } target)
        {
            // Copia drivers para a pasta escolhida: não muda nada no Windows.
            var snap = new SnapshotCollector(ctx.GameProfiles).Collect(new CollectOptions { SampleSeconds = 1, Network = false });
            File.WriteAllText(full + ".result", JsonSerializer.Serialize(DriverBackup.Export(target, snap), FpsxJson.Options));
            return 0;
        }
        else if (request.Action == "system-check")
        {
            // Só leitura: o resultado volta em JSON no lugar do id de sessão.
            var report = WindowsRepair.Check();
            File.WriteAllText(full + ".result", JsonSerializer.Serialize(report, FpsxJson.Options));
            return 0;
        }
        else if (request.Action == "apply")
        {
            // Scan novo, já elevado: aplicar em cima do estado da tela do outro
            // processo seria confiar em informação velha.
            var snapshot = new SnapshotCollector(ctx.GameProfiles).Collect(new CollectOptions { SampleSeconds = 1, Network = false });
            // Driver: pergunta de novo ao Windows Update aqui dentro, em vez de
            // confiar na lista que o app comum mostrou. Só entra o que ele oferece agora.
            if (request.Ids.Any(id => id.StartsWith("driver-update", StringComparison.OrdinalIgnoreCase)))
                snapshot = snapshot with { PendingDrivers = WindowsUpdateDrivers.SearchAsync().GetAwaiter().GetResult() };
            // Kit de drivers: confere a pasta de novo aqui dentro antes de a proposta existir.
            if (request.Folder is { } kit && request.Ids.Contains("driver-kit-install") && DriverBackup.Check(kit) is null)
                snapshot = snapshot with { DriverKitFolder = kit };
            var scan = new DecisionEngine(ctx.Catalog, ctx.GameProfiles).Evaluate(snapshot, ctx.Settings.Profile, ctx.License().Plan);
            session = new OptimizationEngine(system, ctx.Store).Apply(scan, request.Ids, new ApplyOptions
            {
                AllowExperimental = request.Experimental,
                OnFailure = Dialogs.Failure,
                Trial = ctx.TrialLimit(),
            });
        }
        else
        {
            return 2;
        }

        ctx.RecordSession(session);
        File.WriteAllText(full + ".result", session.Id);
        return 0;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
