using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fpsx.Core.Json;

namespace Fpsx.Client;

public static class LicenseKeys
{
    /// <summary>
    /// Chave PUBLICA que verifica licenca e catalogo vindos do servidor. A
    /// privada correspondente fica so' no secret do deploy
    /// (LICENSE_PRIVATE_KEY_PEM). Trocar a chave = gerar par novo com
    /// `python -m app.signing`, colar a publica aqui e publicar versao nova do app.
    /// </summary>
    public const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEKXLdL2NKBr5aqW1WPhRN0DtLr8b9
        BaWsfpfcBlC/7JVkKFNq8h0KP6y7YyoCUJe8r6g6P0jllVZsmunAUmGeng==
        -----END PUBLIC KEY-----
        """;
}

/// <summary>Conteúdo assinado da licença (espelho de licenses.build_token_payload no backend).</summary>
public sealed record LicensePayload
{
    public int V { get; init; }
    public int Uid { get; init; }
    public string Email { get; init; } = "";
    public string Plan { get; init; } = "free";
    public string PlanKey { get; init; } = "free";
    public string Status { get; init; } = "free";
    public int? LicenseId { get; init; }
    public string Device { get; init; } = "";
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset ValidUntil { get; init; }
}

/// <summary>
/// Verificação de token no formato base64url(json).base64url(assinatura DER),
/// ECDSA P-256 com SHA-256. É o mesmo formato de app/signing.py.
/// </summary>
public static class SignedToken
{
    public static byte[]? VerifyBody(string token, string publicKeyPem = LicenseKeys.PublicKeyPem)
    {
        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1)
            return null;
        try
        {
            var body = FromBase64Url(token[..dot]);
            var signature = FromBase64Url(token[(dot + 1)..]);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);
            return ecdsa.VerifyData(body, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence) ? body : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public static T? Verify<T>(string token, string publicKeyPem = LicenseKeys.PublicKeyPem)
    {
        var body = VerifyBody(token, publicKeyPem);
        if (body is null)
            return default;
        try
        {
            return JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(body), FpsxJson.Options);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public static byte[] FromBase64Url(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b + new string('=', (4 - b.Length % 4) % 4));
    }
}

public sealed record LicenseState
{
    public string Plan { get; init; } = "free";
    public string Status { get; init; } = "free";
    public string? Email { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? ValidUntil { get; init; }

    /// <summary>Por que o app está em Free, quando está (mostrado na tela Conta).</summary>
    public string? Notice { get; init; }

    public bool LoggedIn => Email is not null;

    /// <summary>Dias de aviso antes do vencimento (plano pago ou teste).</summary>
    public const int RenewalWarningDays = 3;

    /// <summary>Plano que existiu e venceu: o app caiu para Free, e o resumo do que foi feito aparece.</summary>
    public bool Expired => Status == "expired";

    /// <summary>Dias que faltam (arredondado para cima), ou null sem vencimento.</summary>
    public int? DaysLeft(DateTimeOffset now) =>
        ExpiresAt is { } e ? Math.Max(0, (int)Math.Ceiling((e - now).TotalDays)) : null;

    /// <summary>Plano pago ou teste vencendo nos próximos dias: hora de avisar, uma vez por abertura do app.</summary>
    public bool EndingSoon(DateTimeOffset now) =>
        Status is "active" or "trial" && DaysLeft(now) is { } d && d <= RenewalWarningDays;

    public static LicenseState Free(string? notice = null) => new() { Notice = notice };

    /// <summary>
    /// Plano efetivo: só vale o que está assinado, é deste PC e ainda está
    /// dentro da carência offline. Qualquer outra coisa cai para Free.
    /// </summary>
    public static LicenseState From(string? token, string deviceHash, DateTimeOffset now, string publicKeyPem = LicenseKeys.PublicKeyPem)
    {
        if (string.IsNullOrEmpty(token))
            return Free();
        var p = SignedToken.Verify<LicensePayload>(token, publicKeyPem);
        if (p is null)
            return Free("A licença salva não é válida. Entre na sua conta de novo.");
        if (!string.Equals(p.Device, deviceHash, StringComparison.OrdinalIgnoreCase))
            return Free("A licença salva pertence a outro PC.");
        if (p.ValidUntil <= now)
            return new LicenseState { Email = p.Email, Notice = "Conecte à internet para revalidar sua licença. Enquanto isso o RKZFPS funciona no plano Free." };
        if (p.Status is "expired" or "blocked")
            return new LicenseState { Email = p.Email, Status = p.Status, Notice = p.Status == "blocked" ? "Licença bloqueada. Fale com o suporte." : "Sua assinatura venceu." };

        return new LicenseState { Plan = p.Plan, Status = p.Status, Email = p.Email, ExpiresAt = p.ExpiresAt, ValidUntil = p.ValidUntil };
    }
}
