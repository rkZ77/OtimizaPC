using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fpsx.Core.Diagnostics;
using Fpsx.Core.Engine;
using Fpsx.Core.Json;
using Fpsx.Core.Model;

namespace Fpsx.Windows;

/// <summary>Resultado do "Preparar formatação".</summary>
public sealed record DriverExportResult(bool Ok, string Folder, int DriverCount, string? StorageWarning, string Message);

/// <summary>
/// Kit de drivers para formatar: exporta com o pnputil do Windows os drivers
/// de terceiros que este PC usa (rede, áudio, chipset, vídeo), grava o
/// manifesto com o SHA-256 de cada arquivo e um LEIA-ME com as peças. Depois
/// de formatar, reinstala do mesmo kit, só se nada tiver sido trocado.
/// </summary>
public static class DriverBackup
{
    private static string PnpUtil => Path.Combine(Environment.SystemDirectory, "pnputil.exe");

    /// <summary>Precisa de administrador (o pnputil lê o repositório de drivers do Windows).</summary>
    public static DriverExportResult Export(string targetFolder, SystemSnapshot snapshot)
    {
        if (!Path.IsPathFullyQualified(targetFolder) || !Directory.Exists(targetFolder))
            return new DriverExportResult(false, targetFolder, 0, null, "Escolha uma pasta que exista (de preferência num pendrive ou em outro disco).");

        var kit = Path.Combine(targetFolder, $"RKZFPS drivers {Environment.MachineName} {DateTime.Now:yyyy-MM-dd HHmm}");
        var drivers = Path.Combine(kit, DriverKit.DriversFolder);
        Directory.CreateDirectory(drivers);

        var r = Run(PnpUtil, ["/export-driver", "*", drivers]);
        var files = Directory.EnumerateFiles(drivers, "*", SearchOption.AllDirectories).ToList();
        var infs = files.Count(f => f.EndsWith(".inf", StringComparison.OrdinalIgnoreCase));
        if (infs == 0)
            return new DriverExportResult(false, kit, 0, null, $"O Windows não exportou nenhum driver (código {r.ExitCode}). Confira se há espaço na pasta escolhida.");

        var storage = StorageWarning();
        var fp = HardwareFingerprint.From(snapshot);
        var manifest = new DriverKitManifest
        {
            CreatedAt = DateTimeOffset.Now,
            MachineName = Environment.MachineName,
            Board = fp.Board,
            Cpu = fp.Cpu,
            Gpu = fp.Gpu,
            DriverCount = infs,
            StorageWarning = storage,
            Files = files.Select(f => new DriverKitFile(Path.GetRelativePath(kit, f), Sha256(f))).ToList(),
        };
        File.WriteAllText(Path.Combine(kit, DriverKit.ManifestName), JsonSerializer.Serialize(manifest, FpsxJson.Options));
        File.WriteAllText(Path.Combine(kit, "LEIA-ME.txt"), ReadMe(manifest, snapshot), new UTF8Encoding(true));

        return new DriverExportResult(true, kit, infs, storage,
            $"{infs} drivers salvos em {kit}. Guarde esta pasta fora do disco do Windows (pendrive ou outro disco): formatar apaga o disco do Windows.");
    }

    /// <summary>Lê o manifesto e confere todos os arquivos. null = kit válido; senão, o motivo.</summary>
    public static string? Check(string folder)
    {
        var manifestPath = Path.Combine(folder, DriverKit.ManifestName);
        if (!File.Exists(manifestPath))
            return "Esta pasta não é um kit de drivers do RKZFPS. Escolha a pasta \"RKZFPS drivers ...\" que foi salva antes de formatar.";
        DriverKitManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<DriverKitManifest>(File.ReadAllText(manifestPath), FpsxJson.Options);
        }
        catch (JsonException)
        {
            return "O arquivo de controle do kit está corrompido. Nada foi instalado.";
        }

        if (manifest is null)
            return "O arquivo de controle do kit está vazio. Nada foi instalado.";
        var driversDir = Path.Combine(folder, DriverKit.DriversFolder);
        var actual = Directory.Exists(driversDir)
            ? Directory.EnumerateFiles(driversDir, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f))
            : [];
        return DriverKit.Verify(manifest, rel =>
        {
            var full = Path.GetFullPath(Path.Combine(folder, rel));
            return full.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase) && File.Exists(full) ? Sha256(full) : null;
        }, actual);
    }

    /// <summary>Instala o kit (administrador). Confere de novo aqui dentro: a tela pode ter visto outra pasta.</summary>
    public static CommandResult Install(string folder)
    {
        if (Check(folder) is { } problem)
            return new CommandResult(2, problem);
        var r = Run(PnpUtil, ["/add-driver", Path.Combine(folder, DriverKit.DriversFolder, "*.inf"), "/subdirs", "/install"]);
        // 3010: instalou e pede reinício. 259: nada novo para instalar (os drivers já estavam lá).
        return r.ExitCode switch
        {
            0 => new CommandResult(0, "Drivers do kit instalados. Reinicie o PC para concluir."),
            3010 => new CommandResult(0, "Drivers do kit instalados. Reinicie o PC para concluir."),
            259 => new CommandResult(0, "Os drivers do kit já estavam instalados neste PC."),
            _ => new CommandResult(r.ExitCode, $"O Windows não instalou os drivers do kit (código {r.ExitCode})."),
        };
    }

    /// <summary>
    /// Intel VMD ou RST: o instalador do Windows pode não mostrar o SSD sem o
    /// driver da controladora. Lido do nome da controladora no Windows.
    /// </summary>
    private static string? StorageWarning()
    {
        var names = Wmi.Query("SELECT Name FROM Win32_SCSIController")
            .Select(r => r.GetValueOrDefault("Name")?.ToString() ?? "")
            .Where(n => n.Length > 0)
            .ToList();
        var hit = names.FirstOrDefault(n => n.Contains("VMD", StringComparison.OrdinalIgnoreCase)
                                            || n.Contains("RST", StringComparison.OrdinalIgnoreCase)
                                            || n.Contains("Rapid Storage", StringComparison.OrdinalIgnoreCase));
        return hit is null ? null
            : $"Este PC usa a controladora \"{hit}\". No instalador do Windows o SSD pode não aparecer: clique em Carregar driver e aponte para a pasta drivers deste kit (copie para o pendrive de instalação).";
    }

    private static string ReadMe(DriverKitManifest m, SystemSnapshot s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("KIT DE DRIVERS DO RKZFPS");
        sb.AppendLine($"Salvo em {m.CreatedAt:dd/MM/yyyy HH:mm} no PC {m.MachineName}.");
        sb.AppendLine();
        sb.AppendLine("PEÇAS DESTE PC");
        sb.AppendLine($"Placa-mãe: {m.Board}");
        sb.AppendLine($"Processador: {m.Cpu}");
        foreach (var g in s.Gpus)
            sb.AppendLine($"Vídeo: {g.Name} (driver {g.DriverVersion})");
        foreach (var d in s.Disks.Where(d => d.Model.Length > 0))
            sb.AppendLine($"Disco: {d.Model}{(d.IsSystemDrive ? " (Windows)" : "")}");
        sb.AppendLine($"Drivers salvos: {m.DriverCount}");
        sb.AppendLine();
        if (m.StorageWarning is { } w)
        {
            sb.AppendLine("ATENÇÃO");
            sb.AppendLine(w);
            sb.AppendLine();
        }
        sb.AppendLine("DEPOIS DE FORMATAR");
        sb.AppendLine("1. Instale o RKZFPS e entre na sua conta.");
        sb.AppendLine("2. Página Drivers e reparo, Reinstalar drivers salvos, escolha esta pasta.");
        sb.AppendLine("Sem o RKZFPS: Gerenciador de Dispositivos, clique com o botão direito no aparelho,");
        sb.AppendLine("Atualizar driver, Procurar no computador, e aponte para a pasta drivers.");
        sb.AppendLine();
        sb.AppendLine("Não apague nem troque arquivos desta pasta: o RKZFPS confere cada um antes de instalar.");
        return sb.ToString();
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static CommandResult Run(string exe, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o pnputil.");
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(TimeSpan.FromMinutes(20)))
        {
            p.Kill(entireProcessTree: true);
            return new CommandResult(-1, "Tempo esgotado.");
        }

        return new CommandResult(p.ExitCode, output.Result + error.Result);
    }
}
