using Fpsx.Core.Model;

namespace Fpsx.Core.Diagnostics;

/// <summary>
/// Resultado da verificação dos arquivos do Windows (só leitura): estado da
/// imagem do Windows (DISM ScanHealth) e do disco do sistema (Repair-Volume
/// -Scan). Os valores vêm do Windows em inglês fixo, sem depender do idioma.
/// </summary>
public sealed record SystemHealthReport(string Image, string Disk)
{
    public bool ImageRepairable => Image.Equals("Repairable", StringComparison.OrdinalIgnoreCase);
    public bool ImageBroken => Image.Equals("NonRepairable", StringComparison.OrdinalIgnoreCase);
    public bool ImageOk => Image.Equals("Healthy", StringComparison.OrdinalIgnoreCase);
    public bool DiskOk => Disk.Equals("NoErrorsFound", StringComparison.OrdinalIgnoreCase);
    public bool DiskUnknown => Disk.Length == 0 || Disk.Equals("Unknown", StringComparison.OrdinalIgnoreCase);

    /// <summary>Lê as linhas IMG=... e DISK=... que o script de verificação imprime.</summary>
    public static SystemHealthReport Parse(string output)
    {
        string Value(string key) => output.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))?[(key.Length + 1)..].Trim() ?? "Unknown";
        return new SystemHealthReport(Value("IMG"), Value("DISK"));
    }

    public HealthStatus Status => ImageBroken || ImageRepairable || !DiskOk && !DiskUnknown ? HealthStatus.Problem
        : ImageOk && (DiskOk || DiskUnknown) ? HealthStatus.Ok
        : HealthStatus.Unknown;

    public string Title => Status switch
    {
        HealthStatus.Ok => "Arquivos do Windows em ordem",
        HealthStatus.Problem => "Encontramos arquivos do Windows com problema",
        _ => "Não deu para concluir a verificação",
    };

    public string Detail
    {
        get
        {
            var image = ImageOk ? "Imagem do Windows: saudável."
                : ImageRepairable ? "Imagem do Windows: corrompida, e dá para reparar pelo botão abaixo."
                : ImageBroken ? "Imagem do Windows: corrompida demais para o reparo automático. O caminho é reinstalar o Windows mantendo os arquivos (Configurações > Sistema > Recuperação)."
                : "Imagem do Windows: não foi possível ler.";
            var disk = DiskOk ? "Disco do sistema: sem erros."
                : DiskUnknown ? "Disco do sistema: não foi possível verificar."
                : "Disco do sistema: o Windows achou erros no sistema de arquivos. Rode \"chkdsk C: /f\" e reinicie; se voltar, faça backup e confira a saúde do disco.";
            return image + " " + disk;
        }
    }
}
