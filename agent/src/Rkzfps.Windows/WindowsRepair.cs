using System.Diagnostics;
using System.Text;
using Rkzfps.Core.Diagnostics;
using Rkzfps.Core.Engine;
using Rkzfps.Core.Model;

namespace Rkzfps.Windows;

/// <summary>
/// Verificação e reparo dos arquivos do Windows, e ponto de restauração.
/// Scripts FIXOS, sem nada vindo de fora no texto do comando: o que varia é
/// escolhido de uma lista fechada (SystemRepairKind). PowerShell pelo caminho
/// absoluto do System32, como o ipconfig e o netsh do reparo de rede.
/// </summary>
public static class WindowsRepair
{
    private static string PowerShell => Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    // Os cmdlets devolvem enums em inglês fixo (Healthy, Repairable,
    // NoErrorsFound): a leitura não depende do idioma do Windows.
    private const string CheckScript =
        "$ErrorActionPreference='Stop';" +
        "try { 'IMG=' + (Repair-WindowsImage -Online -ScanHealth -NoRestart).ImageHealthState } catch { 'IMG=Unknown' };" +
        "try { 'DISK=' + (Repair-Volume -DriveLetter $env:SystemDrive.Substring(0,1) -Scan) } catch { 'DISK=Unknown' }";

    private const string RestoreHealthScript =
        "$ErrorActionPreference='Stop';" +
        "try { $s = (Repair-WindowsImage -Online -RestoreHealth -NoRestart).ImageHealthState; 'IMG=' + $s; if ($s -ne 'Healthy') { exit 1 } } catch { $_.Exception.Message; exit 2 }";

    private const string RestorePointScript =
        "$ErrorActionPreference='Stop';" +
        "try { Checkpoint-Computer -Description 'RKZFPS: antes de instalar driver' -RestorePointType MODIFY_SETTINGS; 'ok' } catch { $_.Exception.Message; exit 1 }";

    /// <summary>Só leitura. Precisa de administrador e leva alguns minutos (o DISM varre a imagem inteira).</summary>
    public static SystemHealthReport Check()
    {
        var r = Run(PowerShell, ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", CheckScript], TimeSpan.FromMinutes(40));
        return SystemHealthReport.Parse(r.Output);
    }

    public static CommandResult Repair(SystemRepairKind kind) => kind switch
    {
        SystemRepairKind.ImageRestoreHealth => Friendly(
            Run(PowerShell, ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", RestoreHealthScript], TimeSpan.FromMinutes(90)),
            ok: "Imagem do Windows reparada e saudável."),
        // O SFC escreve em UTF-16 e em pt-BR: vale o código de saída, e o
        // detalhe fica no log que o próprio Windows grava.
        SystemRepairKind.SystemFileScan => SfcResult(
            Run(Path.Combine(Environment.SystemDirectory, "sfc.exe"), ["/scannow"], TimeSpan.FromMinutes(90), Encoding.Unicode)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static CommandResult CreateRestorePoint() =>
        Run(PowerShell, ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", RestorePointScript], TimeSpan.FromMinutes(5));

    private static CommandResult Friendly(CommandResult r, string ok) =>
        r.ExitCode == 0 ? r with { Output = ok } : r;

    private static CommandResult SfcResult(CommandResult r) => r.ExitCode switch
    {
        -1 => r,
        _ => new CommandResult(0, $"Verificação de arquivos do sistema concluída (código {r.ExitCode}). O relatório do Windows fica em " + @"C:\Windows\Logs\CBS\CBS.log."),
    };

    private static CommandResult Run(string exe, IReadOnlyList<string> args, TimeSpan timeout, Encoding? encoding = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (encoding is not null)
        {
            psi.StandardOutputEncoding = encoding;
            psi.StandardErrorEncoding = encoding;
        }
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Não foi possível iniciar {Path.GetFileName(exe)}.");
        // Leitura assíncrona: o DISM e o SFC escrevem progresso por minutos, e
        // ler só no fim encheria o buffer e travaria o processo.
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            return new CommandResult(-1, "Tempo esgotado.");
        }

        return new CommandResult(process.ExitCode, output.Result + error.Result);
    }
}
