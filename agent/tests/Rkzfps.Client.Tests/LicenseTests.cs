using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rkzfps.Core.Catalog;
using Rkzfps.Core.Model;

namespace Rkzfps.Client.Tests;

/// <summary>Assina no mesmo formato de app/signing.py, com uma chave de teste.</summary>
internal sealed class TestSigner : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public string PublicPem => _key.ExportSubjectPublicKeyInfoPem();

    public string Sign(object payload)
    {
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        var sig = _key.SignData(body, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return B64(body) + "." + B64(sig);
    }

    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Dispose() => _key.Dispose();
}

public class LicenseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private const string Device = "abc123";

    private static object Payload(string plan = "pro", string status = "active", string device = Device, int validDays = 7) => new
    {
        v = 1, uid = 7, email = "cliente@rkzfps.app", plan, plan_key = plan, status, device,
        issued_at = Now, expires_at = Now.AddDays(30), valid_until = Now.AddDays(validDays),
    };

    [Fact]
    public void Token_valido_libera_o_plano()
    {
        using var s = new TestSigner();
        var state = LicenseState.From(s.Sign(Payload()), Device, Now, s.PublicPem);
        Assert.Equal("pro", state.Plan);
        Assert.Equal("cliente@rkzfps.app", state.Email);
        Assert.Null(state.Notice);
    }

    [Fact]
    public void Fim_do_teste_so_e_reconhecido_quando_o_servidor_assina()
    {
        using var s = new TestSigner();
        var fimDoTeste = new
        {
            v = 1, uid = 7, email = "cliente@rkzfps.app", plan = "free", plan_key = "free", status = "expired", device = Device,
            issued_at = Now, expires_at = Now.AddDays(-1), valid_until = Now.AddDays(7), ended_trial = true,
        };
        var state = LicenseState.From(s.Sign(fimDoTeste), Device, Now, s.PublicPem);
        Assert.True(state.TrialEnded);
        Assert.Equal("free", state.Plan);
        Assert.Equal("Seu teste grátis terminou.", state.Notice);

        // Plano pago vencido: sem o sinal, nada é desfeito.
        var pagoVencido = LicenseState.From(s.Sign(Payload("free", "expired")), Device, Now, s.PublicPem);
        Assert.True(pagoVencido.Expired);
        Assert.False(pagoVencido.TrialEnded);

        // Teste ainda valendo, mesmo com o campo: não desfaz.
        var ativo = LicenseState.From(s.Sign(new
        {
            v = 1, uid = 7, email = "cliente@rkzfps.app", plan = "pro", plan_key = "pro", status = "trial", device = Device,
            issued_at = Now, expires_at = Now.AddDays(3), valid_until = Now.AddDays(3), ended_trial = true,
        }), Device, Now, s.PublicPem);
        Assert.False(ativo.TrialEnded);

        // Token de outro PC nunca dispara o desfazer.
        Assert.False(LicenseState.From(s.Sign(fimDoTeste), "outro-pc", Now, s.PublicPem).TrialEnded);
    }

    [Fact]
    public void Token_adulterado_cai_para_free()
    {
        using var s = new TestSigner();
        var token = s.Sign(Payload("starter"));
        var forgedBody = s.Sign(Payload("ultimate")).Split('.')[0];
        var state = LicenseState.From(forgedBody + "." + token.Split('.')[1], Device, Now, s.PublicPem);
        Assert.Equal("free", state.Plan);
        Assert.NotNull(state.Notice);
    }

    [Fact]
    public void Token_assinado_por_outra_chave_cai_para_free()
    {
        using var real = new TestSigner();
        using var attacker = new TestSigner();
        Assert.Equal("free", LicenseState.From(attacker.Sign(Payload("ultimate")), Device, Now, real.PublicPem).Plan);
    }

    [Fact]
    public void Token_de_outro_PC_cai_para_free()
    {
        using var s = new TestSigner();
        var state = LicenseState.From(s.Sign(Payload(device: "outro-pc")), Device, Now, s.PublicPem);
        Assert.Equal("free", state.Plan);
        Assert.Contains("outro PC", state.Notice);
    }

    [Fact]
    public void Carencia_offline_vencida_cai_para_free_mas_mantem_a_conta()
    {
        using var s = new TestSigner();
        var state = LicenseState.From(s.Sign(Payload(validDays: -1)), Device, Now, s.PublicPem);
        Assert.Equal("free", state.Plan);
        Assert.True(state.LoggedIn);
        Assert.Contains("internet", state.Notice);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("blocked")]
    public void Licenca_vencida_ou_bloqueada_cai_para_free(string status)
    {
        using var s = new TestSigner();
        Assert.Equal("free", LicenseState.From(s.Sign(Payload(status: status)), Device, Now, s.PublicPem).Plan);
    }

    [Fact]
    public void Chave_publica_embutida_e_valida()
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(LicenseKeys.PublicKeyPem);
        Assert.Equal(256, ecdsa.KeySize);
    }

    [Fact]
    public void Hash_do_dispositivo_e_estavel_e_nao_expoe_a_origem()
    {
        var a = DeviceIdentity.HashOf("4c4c4544-0042-3510-8052-b4c04f4d3232");
        Assert.Equal(a, DeviceIdentity.HashOf(" 4C4C4544-0042-3510-8052-B4C04F4D3232 "));
        Assert.Equal(64, a.Length);
        Assert.DoesNotContain("4c4c4544", a);
        Assert.Equal(64, DeviceIdentity.Hash().Length);
    }
}

public class OverrideTests
{
    private static OptimizationCatalog Catalog() => OptimizationCatalog.Load(Path.Combine(AppContext.BaseDirectory, "catalog"));

    [Fact]
    public void Override_muda_so_metadado_de_id_conhecido()
    {
        var catalog = CatalogOverrides.Apply(Catalog(),
        [
            new CatalogOverride { Kind = "optimization", Id = "power-plan-high-performance", Enabled = false, Risk = RiskLevel.Medium, MinPlan = "pro" },
            new CatalogOverride { Kind = "optimization", Id = "turbo-fps-magico", Enabled = true },
            new CatalogOverride { Kind = "optimization", Id = "game-mode-enable", MinPlan = "plano-inventado" },
        ]);

        var hp = catalog.Find("power-plan-high-performance")!;
        Assert.False(hp.Enabled);
        Assert.Equal(RiskLevel.Medium, hp.Risk);
        Assert.Equal("pro", hp.MinPlan);
        Assert.Null(catalog.Find("turbo-fps-magico"));
        // Plano inventado no override não muda nada: fica o do catálogo.
        Assert.Equal(Catalog().Find("game-mode-enable")!.MinPlan, catalog.Find("game-mode-enable")!.MinPlan);
        Assert.Equal(Catalog().Optimizations.Count, catalog.Optimizations.Count);
    }

    [Fact]
    public void Override_sem_assinatura_valida_e_ignorado()
    {
        using var real = new TestSigner();
        using var attacker = new TestSigner();
        var payload = new { v = 1, issued_at = DateTimeOffset.UtcNow, overrides = new[] { new { kind = "optimization", id = "hags-enable", min_plan = "free" } } };

        var forged = CatalogOverrides.ApplySigned(Catalog(), attacker.Sign(payload), real.PublicPem);
        Assert.Equal("ultimate", forged.Find("hags-enable")!.MinPlan);

        var genuine = CatalogOverrides.ApplySigned(Catalog(), real.Sign(payload), real.PublicPem);
        Assert.Equal("free", genuine.Find("hags-enable")!.MinPlan);
    }
}

public class StorageTests
{
    [Fact]
    public void Token_fica_protegido_pelo_usuario_do_windows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rkzfps-client-tests", Guid.NewGuid().ToString("N"));
        var storage = new ClientStorage(dir);
        storage.SaveToken("corpo.assinatura");
        Assert.Equal("corpo.assinatura", storage.LoadToken());
        Assert.DoesNotContain("corpo", File.ReadAllText(Path.Combine(dir, "license.token")));
        storage.DeleteToken();
        Assert.Null(storage.LoadToken());
    }

    [Fact]
    public void Fila_de_telemetria_envia_em_lotes_e_descarta_o_enviado()
    {
        var storage = new ClientStorage(Path.Combine(Path.GetTempPath(), "rkzfps-client-tests", Guid.NewGuid().ToString("N")));
        for (var i = 0; i < 5; i++)
            storage.Enqueue(new TelemetryEvent { Event = "scan_completed", Detail = new() { ["problems"] = i } });
        Assert.Equal(3, storage.PeekQueue(3).Count);
        storage.DropFromQueue(3);
        Assert.Equal(2, storage.PeekQueue(100).Count);
    }
}
