using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Rkzfps.Core.Json;

namespace Rkzfps.Client;

/// <summary>Versão do app publicada pelo admin, como veio ASSINADA do servidor.</summary>
public sealed record SignedRelease
{
    public string Component { get; init; } = "";
    public string Version { get; init; } = "";
    public string Url { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string Notes { get; init; } = "";
}

/// <summary>
/// Atualização do app com um clique. Quatro travas antes de rodar qualquer
/// instalador baixado:
///   1. os dados da versão vêm assinados pelo servidor (chave ECDSA embutida no app);
///   2. o download é só por HTTPS e só do GitHub do projeto;
///   3. o SHA-256 do arquivo tem que bater com o assinado;
///   4. só atualiza para versão MAIOR que a instalada (nada de "atualizar" para uma velha com falha).
/// </summary>
public static class Updater
{
    // Onde os instaladores ficam publicados. O GitHub redireciona o download
    // para os servidores de arquivos dele: esses também são aceitos.
    private static readonly string[] AllowedHosts = ["github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"];
    private const string AllowedPath = "/rkZ77/OtimizaPC/releases/download/";

    /// <summary>Versão nova e válida, ou null (sem novidade, assinatura inválida ou link fora do permitido).</summary>
    public static SignedRelease? Check(string? signedToken, string currentVersion, string publicKeyPem = LicenseKeys.PublicKeyPem)
    {
        if (string.IsNullOrEmpty(signedToken))
            return null;
        var release = SignedToken.Verify<SignedRelease>(signedToken, publicKeyPem);
        if (release is null || release.Component != "agent")
            return null;
        if (!Version.TryParse(release.Version, out var latest) || !Version.TryParse(currentVersion.Split('+')[0], out var mine) || latest <= mine)
            return null;
        if (!IsAllowedDownload(release.Url) || release.Sha256.Length != 64)
            return null;
        return release;
    }

    public static bool IsAllowedDownload(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttps
        && u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && u.AbsolutePath.StartsWith(AllowedPath, StringComparison.Ordinal)
        && u.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedRedirect(Uri u) =>
        u.Scheme == Uri.UriSchemeHttps && AllowedHosts.Any(h => u.Host.Equals(h, StringComparison.OrdinalIgnoreCase));

    public static bool HashMatches(string file, string expectedSha256)
    {
        using var stream = File.OpenRead(file);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        return actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Baixa o instalador para a pasta temporária e confere o hash. Arquivo que não bate é apagado.</summary>
    public static async Task<string> DownloadAsync(SignedRelease release, IProgress<int>? percent = null, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "RKZFPS-update");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"RKZFPS-Setup-{release.Version}.exe");

        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(15) };
        using var response = await http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } final && !IsAllowedRedirect(final))
            throw new InvalidOperationException("O download foi redirecionado para um endereço não permitido. Atualização cancelada.");

        var total = response.Content.Headers.ContentLength;
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var target = File.Create(file))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total is > 0)
                    percent?.Report((int)(done * 100 / total.Value));
            }
        }

        if (!HashMatches(file, release.Sha256))
        {
            File.Delete(file);
            throw new InvalidOperationException("O arquivo baixado não confere com a versão publicada. Atualização cancelada por segurança.");
        }

        return file;
    }

    /// <summary>
    /// Roda o instalador em modo silencioso. Ele fecha o RKZFPS, atualiza no
    /// mesmo lugar (mesmo modo de instalação de antes) e reabre o app.
    /// Histórico, backups e configurações ficam na pasta de dados e não são tocados.
    /// </summary>
    public static void Install(string installer)
    {
        var psi = new ProcessStartInfo(installer) { UseShellExecute = true };
        foreach (var arg in new[] { "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", ScopeArg(Environment.ProcessPath ?? "") })
            psi.ArgumentList.Add(arg);
        Process.Start(psi);
    }

    /// <summary>
    /// Atualiza no MESMO tipo de instalação que está rodando. Sem isso o
    /// instalador usa o padrão (todos os usuários, pede administrador) e quem
    /// tinha instalado só para si ficava com duas cópias do RKZFPS.
    /// </summary>
    public static string ScopeArg(string runningExe, string? localAppData = null)
    {
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var perUser = Path.GetFullPath(Path.Combine(localAppData, "Programs")) + Path.DirectorySeparatorChar;
        return runningExe.StartsWith(perUser, StringComparison.OrdinalIgnoreCase) ? "/CURRENTUSER" : "/ALLUSERS";
    }
}
