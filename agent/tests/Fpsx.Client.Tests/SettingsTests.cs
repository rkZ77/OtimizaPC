using Fpsx.Client;

namespace Fpsx.Client.Tests;

public class SettingsTests
{
    [Fact]
    public void Endereco_antigo_salvo_nas_configuracoes_e_ignorado()
    {
        // O arquivo real que travou o login do dono: versão antiga gravou o
        // domínio provisório e todo login ia para um servidor inexistente.
        var dir = Path.Combine(Path.GetTempPath(), "fpsx-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"),
            "{ \"api_url\": \"https://fpsx-production.up.railway.app\", \"telemetry_consent\": true, \"profile\": \"gaming\" }");

        var storage = new ClientStorage(dir);
        var settings = storage.LoadSettings();
        Assert.Equal(ClientSettings.ProductionApiUrl, settings.ApiUrl);
        Assert.Equal("gaming", settings.Profile);

        // E ao salvar de novo, o endereço não volta para o arquivo.
        storage.SaveSettings(settings);
        Assert.DoesNotContain("api_url", File.ReadAllText(Path.Combine(dir, "settings.json")));
    }

    [Fact]
    public void Partida_so_sobe_com_o_consentimento_que_fala_de_partidas()
    {
        Assert.False(new ClientSettings { TelemetryConsent = true, TelemetryConsentVersion = 1 }.GameplayConsent);
        Assert.False(new ClientSettings { TelemetryConsent = false, TelemetryConsentVersion = 2 }.GameplayConsent);
        Assert.False(new ClientSettings().GameplayConsent);
        Assert.True(new ClientSettings { TelemetryConsent = true, TelemetryConsentVersion = ClientSettings.CurrentConsentVersion }.GameplayConsent);
    }
}
