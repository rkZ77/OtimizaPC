namespace Fpsx.Core.Diagnostics;

/// <summary>Um arquivo do kit, pelo caminho relativo à pasta do kit, com o SHA-256.</summary>
public sealed record DriverKitFile(string Path, string Sha256);

/// <summary>
/// O que o "Preparar formatação" grava junto dos drivers: de que PC eles são
/// e o SHA-256 de cada arquivo. Depois de formatar, o app só reinstala se a
/// pasta for exatamente a que ele salvou: nada trocado, nada a mais.
/// </summary>
public sealed record DriverKitManifest
{
    public int Version { get; init; } = 1;
    public DateTimeOffset CreatedAt { get; init; }
    public string MachineName { get; init; } = "";
    public string Board { get; init; } = "";
    public string Cpu { get; init; } = "";
    public string Gpu { get; init; } = "";
    public int DriverCount { get; init; }

    /// <summary>Controladora de disco que o instalador do Windows pode não reconhecer (Intel VMD ou RST).</summary>
    public string? StorageWarning { get; init; }

    public IReadOnlyList<DriverKitFile> Files { get; init; } = [];
}

public static class DriverKit
{
    public const string ManifestName = "rkzfps-drivers.json";
    public const string DriversFolder = "drivers";

    /// <summary>Caminho relativo e dentro da pasta: sem raiz, sem "..", sem unidade.</summary>
    public static bool SafeRelative(string path) =>
        path.Length is > 0 and < 260
        && !System.IO.Path.IsPathRooted(path)
        && !path.Contains(':')
        && !path.Replace('\\', '/').Split('/').Any(part => part is ".." or "");

    /// <summary>
    /// Confere a pasta contra o manifesto. null = tudo certo; senão, o motivo.
    /// <paramref name="hashOf"/> devolve o SHA-256 do arquivo (ou null se não existe);
    /// <paramref name="actualFiles"/> são os arquivos que existem de fato na pasta de drivers.
    /// </summary>
    public static string? Verify(DriverKitManifest manifest, Func<string, string?> hashOf, IEnumerable<string> actualFiles)
    {
        if (manifest.Version != 1 || manifest.Files.Count == 0)
            return "Esta pasta não tem um kit de drivers do RKZFPS válido.";
        if (manifest.Files.Any(f => !SafeRelative(f.Path)))
            return "O kit tem um caminho de arquivo inválido. Nada foi instalado.";

        var listed = manifest.Files.Select(f => f.Path.Replace('/', '\\')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extra = actualFiles.Select(f => f.Replace('/', '\\')).FirstOrDefault(f => !listed.Contains(f));
        if (extra is not null)
            return $"A pasta tem um arquivo que o RKZFPS não salvou ({extra}). Por segurança, nada foi instalado.";

        foreach (var f in manifest.Files)
        {
            var hash = hashOf(f.Path);
            if (hash is null)
                return $"Falta um arquivo do kit ({f.Path}). Copie a pasta inteira de novo.";
            if (!string.Equals(hash, f.Sha256, StringComparison.OrdinalIgnoreCase))
                return $"Um arquivo do kit foi alterado depois de salvo ({f.Path}). Por segurança, nada foi instalado.";
        }

        return null;
    }
}
