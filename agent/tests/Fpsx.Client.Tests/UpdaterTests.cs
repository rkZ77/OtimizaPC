using System.Security.Cryptography;

namespace Fpsx.Client.Tests;

public class UpdaterTests
{
    private const string Url = "https://github.com/rkZ77/OtimizaPC/releases/download/v0.4.0/FPSX-Setup-0.4.0.exe";
    private static readonly string Hash = new('a', 64);

    private static object Release(string version = "0.4.0", string url = Url, string component = "agent", string? sha = null) =>
        new { component, version, url, sha256 = sha ?? Hash, notes = "Novidades" };

    [Fact]
    public void Versao_nova_assinada_e_oferecida()
    {
        using var s = new TestSigner();
        var r = Updater.Check(s.Sign(Release()), "0.3.0+18f5a0c", s.PublicPem);
        Assert.NotNull(r);
        Assert.Equal("0.4.0", r!.Version);
    }

    [Fact]
    public void Assinatura_de_outra_chave_e_recusada()
    {
        using var real = new TestSigner();
        using var attacker = new TestSigner();
        Assert.Null(Updater.Check(attacker.Sign(Release()), "0.3.0", real.PublicPem));
    }

    [Theory]
    [InlineData("0.3.0")] // mesma versão
    [InlineData("0.2.0")] // versão velha: nunca "atualiza" para trás
    public void Nao_oferece_versao_igual_ou_mais_velha(string offered)
    {
        using var s = new TestSigner();
        Assert.Null(Updater.Check(s.Sign(Release(offered)), "0.3.0", s.PublicPem));
    }

    [Theory]
    [InlineData("http://github.com/rkZ77/OtimizaPC/releases/download/v0.4.0/FPSX-Setup-0.4.0.exe")]
    [InlineData("https://github.com/outro/repo/releases/download/v0.4.0/FPSX-Setup-0.4.0.exe")]
    [InlineData("https://site-falso.com/rkZ77/OtimizaPC/releases/download/v0.4.0/FPSX-Setup-0.4.0.exe")]
    [InlineData("https://github.com/rkZ77/OtimizaPC/releases/download/v0.4.0/script.ps1")]
    public void Link_fora_do_github_do_projeto_e_recusado_mesmo_assinado(string url)
    {
        using var s = new TestSigner();
        Assert.Null(Updater.Check(s.Sign(Release(url: url)), "0.3.0", s.PublicPem));
    }

    [Fact]
    public void Hash_do_arquivo_baixado_tem_que_bater()
    {
        var file = Path.GetTempFileName();
        File.WriteAllText(file, "instalador de verdade");
        var real = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        Assert.True(Updater.HashMatches(file, real.ToLowerInvariant()));
        Assert.False(Updater.HashMatches(file, Hash));
        File.Delete(file);
    }

    [Fact]
    public void Sem_versao_publicada_nao_faz_nada()
    {
        Assert.Null(Updater.Check(null, "0.3.0"));
        Assert.Null(Updater.Check("lixo.lixo", "0.3.0"));
    }
}
