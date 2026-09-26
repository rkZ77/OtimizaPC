using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fpsx.Core.Json;

namespace Fpsx.Client;

/// <summary>Erro da API já com a mensagem em pt-BR que o servidor mandou.</summary>
public sealed class ApiException(HttpStatusCode status, string message, JsonElement? detail = null) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;

    /// <summary>Corpo do "detail" quando é objeto (ex.: lista de PCs no limite de dispositivos).</summary>
    public JsonElement? Detail { get; } = detail;
}

public sealed record DeviceInfo(string DeviceHash, string DeviceName, string WindowsBuild, string AgentVersion);

public sealed record ActivationResult(string Token, LicensePayload License);

public sealed record ReleaseInfo(string Component, string Version, string Url, string Sha256, string Notes);

public sealed record ActiveDevice(int Id, string Name, DateTimeOffset LastSeenAt);

/// <summary>
/// Cliente da API do FPSX. Só lê licença, catálogo e versão, e envia
/// telemetria consentida: não existe chamada que receba comando para executar.
/// </summary>
public sealed class ApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = FpsxJson.Compact;

    public static ApiClient Create(string baseUrl) =>
        new(new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(20) });

    public async Task<string> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var body = await SendAsync(HttpMethod.Post, "api/auth/login", new { email, password }, null, null, ct);
        return body.GetProperty("access_token").GetString()!;
    }

    public async Task<ActivationResult> ActivateAsync(string accessToken, DeviceInfo device, CancellationToken ct = default)
    {
        var body = await SendAsync(HttpMethod.Post, "api/agent/activate", Device(device), accessToken, null, ct);
        return Activation(body);
    }

    public async Task<ActivationResult> RefreshAsync(string deviceToken, DeviceInfo device, CancellationToken ct = default)
    {
        var body = await SendAsync(HttpMethod.Post, "api/agent/refresh", Device(device), null, deviceToken, ct);
        return Activation(body);
    }

    public Task DeactivateAsync(string deviceToken, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/agent/deactivate", new { }, null, deviceToken, ct);

    public async Task<string> CatalogOverridesAsync(string deviceToken, CancellationToken ct = default)
    {
        var body = await SendAsync(HttpMethod.Get, "api/agent/catalog", null, null, deviceToken, ct);
        return body.GetProperty("token").GetString() ?? "";
    }

    public async Task<IReadOnlyList<ReleaseInfo>> ReleasesAsync(CancellationToken ct = default)
    {
        var body = await SendAsync(HttpMethod.Get, "api/agent/releases", null, null, null, ct);
        return body.GetProperty("releases").Deserialize<List<ReleaseInfo>>(FpsxJson.Options) ?? [];
    }

    public async Task<int> SendTelemetryAsync(string deviceToken, IReadOnlyList<TelemetryEvent> events, CancellationToken ct = default)
    {
        var body = await SendAsync(HttpMethod.Post, "api/agent/telemetry", new { events }, null, deviceToken, ct);
        return body.GetProperty("accepted").GetInt32();
    }

    public Task SendBenchmarkAsync(string deviceToken, object benchmark, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/agent/benchmarks", benchmark, null, deviceToken, ct);

    private static object Device(DeviceInfo d) => new
    {
        device_hash = d.DeviceHash,
        device_name = d.DeviceName,
        windows_build = d.WindowsBuild,
        agent_version = d.AgentVersion,
    };

    private static ActivationResult Activation(JsonElement body) => new(
        body.GetProperty("token").GetString()!,
        body.GetProperty("license").Deserialize<LicensePayload>(FpsxJson.Options)!);

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? payload, string? bearer, string? deviceToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (payload is not null)
            request.Content = JsonContent.Create(payload, options: Json);
        if (bearer is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (deviceToken is not null)
            request.Headers.Add("X-Device-Token", deviceToken);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException)
        {
            throw new ApiException(HttpStatusCode.ServiceUnavailable, "Sem conexão com o servidor do FPSX. Verifique sua internet.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ApiException(HttpStatusCode.RequestTimeout, "O servidor do FPSX demorou para responder. Tente de novo.");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            JsonElement root = default;
            if (text.Length > 0)
            {
                try
                {
                    root = JsonDocument.Parse(text).RootElement.Clone();
                }
                catch (JsonException)
                {
                }
            }

            if (response.IsSuccessStatusCode)
                return root;

            // FastAPI manda {"detail": "texto"} ou {"detail": {"message": ..., ...}}.
            // Resposta que nao e' da API (pagina de erro do provedor, servidor
            // errado) diz isso com o codigo: "tente de novo" escondia a causa.
            string message = root.ValueKind == JsonValueKind.Object
                ? "Não foi possível concluir. Tente de novo em instantes."
                : $"O servidor do FPSX não respondeu como esperado (código {(int)response.StatusCode}). Confira sua internet e tente de novo; se continuar, fale com o suporte.";
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
                message = "Algum dado enviado está em formato inválido. Confira o e-mail e a senha.";
            JsonElement? detail = null;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("detail", out var d))
            {
                if (d.ValueKind == JsonValueKind.String)
                    message = d.GetString()!;
                else if (d.ValueKind == JsonValueKind.Object)
                {
                    detail = d;
                    if (d.TryGetProperty("message", out var m))
                        message = m.GetString() ?? message;
                }
            }

            throw new ApiException(response.StatusCode, message, detail);
        }
    }
}

public sealed record TelemetryEvent
{
    public string Event { get; init; } = "";
    public string? OptimizationId { get; init; }
    public bool? Success { get; init; }

    [JsonPropertyName("detail")]
    public Dictionary<string, object> Detail { get; init; } = new();
}
