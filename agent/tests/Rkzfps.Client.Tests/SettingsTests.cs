using Rkzfps.Client;

namespace Rkzfps.Client.Tests;

public class SettingsTests
{
    [Fact]
    public void Endereco_antigo_salvo_nas_configuracoes_e_ignorado()
    {
        // O arquivo real que travou o login do dono: versão antiga gravou o
        // domínio provisório e todo login ia para um servidor inexistente.
        var dir = Path.Combine(Path.GetTempPath(), "rkzfps-settings-" + Guid.NewGuid().ToString("N"));
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
    public void Configuracao_antiga_abre_no_modo_gaming_manual_e_guarda_a_escolha()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rkzfps-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"profile\": \"competitive\", \"auto_measure\": true }");

        var storage = new ClientStorage(dir);
        var old = storage.LoadSettings();
        // Quem atualiza o app não ganha nada automático sem escolher.
        Assert.Equal(Rkzfps.Core.Engine.GamingModeKind.Manual, Rkzfps.Core.Engine.GamingPolicy.Parse(old.GamingMode));
        Assert.Empty(old.GamingAuthorized);

        storage.SaveSettings(old with { GamingMode = "auto", GamingAuthorized = ["power-plan-leave-power-saver"] });
        var back = storage.LoadSettings();
        Assert.Equal("auto", back.GamingMode);
        Assert.Equal(["power-plan-leave-power-saver"], back.GamingAuthorized);
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
