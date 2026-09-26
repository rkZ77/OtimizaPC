using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Fpsx.Client;

/// <summary>
/// Identidade do PC para o limite de dispositivos (seção 36): um hash, e só.
/// Nada de serial de disco, MAC ou nome de usuário. O MachineGuid é gerado
/// pelo Windows na instalação; com o sal do FPSX, o hash não serve para
/// rastrear o PC em nenhum outro serviço.
/// </summary>
public static class DeviceIdentity
{
    private const string Salt = "fpsx-device-v1:";

    public static string Hash()
    {
        var guid = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography")?.GetValue("MachineGuid") as string;
        // Sem MachineGuid (imagem corrompida), cai no nome da máquina: pior,
        // mas estável, e ainda só sai daqui como hash.
        return HashOf(string.IsNullOrWhiteSpace(guid) ? Environment.MachineName : guid);
    }

    internal static string HashOf(string source) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Salt + source.Trim().ToLowerInvariant()))).ToLowerInvariant();

    /// <summary>Nome amigável mostrado na lista "Meus PCs" do site.</summary>
    public static string DisplayName() => Environment.MachineName;
}
