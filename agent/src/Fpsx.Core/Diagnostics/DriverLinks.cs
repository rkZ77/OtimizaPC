using Fpsx.Core.Model;

namespace Fpsx.Core.Diagnostics;

/// <summary>
/// Onde atualizar cada driver DESTE PC: o atualizador oficial da placa de
/// vídeo, o driver de chipset do fabricante do processador, a página de
/// suporte da placa-mãe (rede, áudio, chipset) e os drivers opcionais do
/// Windows Update.
///
/// O RKZFPS não baixa nem instala driver: driver de fonte errada é o jeito
/// mais fácil de quebrar ou infectar um PC, e o atualizador do fabricante é
/// quem sabe o modelo certo. Aqui só se escolhe para onde levar a pessoa.
/// </summary>
public static class DriverLinks
{
    // Busca restrita ao site do fabricante, e não link direto para o modelo:
    // os endereços das páginas de produto mudam, e a busca cai sempre na
    // página oficial certa (mesma escolha do BoardSupportUrl).
    private static readonly (string Marca, string Site)[] Fabricantes =
    [
        ("ASUS", "asus.com"), ("MICRO-STAR", "msi.com"), ("MSI", "msi.com"), ("GIGABYTE", "gigabyte.com"),
        ("ASROCK", "asrock.com"), ("BIOSTAR", "biostar.com.tw"), ("DELL", "dell.com"), ("LENOVO", "lenovo.com"),
        ("HP", "hp.com"), ("HEWLETT", "hp.com"), ("ACER", "acer.com"), ("SAMSUNG", "samsung.com"),
        ("POSITIVO", "positivotecnologia.com.br"), ("PCWARE", "pcware.com.br"),
    ];

    public static IReadOnlyList<FindingAction> For(SystemSnapshot s)
    {
        var links = new List<FindingAction>();

        var gpu = s.Gpus.FirstOrDefault(g => !g.LikelyIntegrated) ?? s.Gpus.FirstOrDefault();
        var video = gpu?.Vendor switch
        {
            GpuVendor.Nvidia => new FindingAction("Vídeo: NVIDIA App", "https://www.nvidia.com/pt-br/software/nvidia-app/"),
            GpuVendor.Amd => new FindingAction("Vídeo: AMD Adrenalin", "https://www.amd.com/pt/support/download/drivers.html"),
            GpuVendor.Intel => new FindingAction("Vídeo: assistente da Intel", "https://www.intel.com.br/content/www/br/pt/support/detect.html"),
            _ => null,
        };
        if (video is not null)
            links.Add(video);

        // Chipset: no Ryzen o driver da AMD traz o gerenciamento de energia
        // dos núcleos; no Intel, o assistente da Intel cobre chipset e rede.
        // Se o link já apareceu como vídeo, não repete.
        var cpu = $"{s.Cpu?.Manufacturer} {s.Cpu?.Name}".ToUpperInvariant();
        FindingAction? chipset = cpu.Contains("AMD")
            ? new("Chipset: AMD", "https://www.amd.com/pt/support/download/drivers.html")
            : cpu.Contains("INTEL") ? new("Chipset: assistente da Intel", "https://www.intel.com.br/content/www/br/pt/support/detect.html") : null;
        if (chipset is not null && links.All(l => l.Target != chipset.Target))
            links.Add(chipset);

        if (BoardDriversUrl(s) is { } board)
            links.Add(new FindingAction("Placa-mãe: site do fabricante", board));

        links.Add(FindingAction.WindowsUpdateDrivers);
        return links;
    }

    public static string? BoardDriversUrl(SystemSnapshot s)
    {
        var product = s.BoardProduct.Trim();
        // Placa sem modelo de verdade ("Default string", "To be filled"): não
        // dá para achar a página certa, e uma busca vaga levaria a site de driver genérico.
        if (product.Length == 0 || product.Contains("Default", StringComparison.OrdinalIgnoreCase)
            || product.Contains("To be filled", StringComparison.OrdinalIgnoreCase))
            return null;

        var maker = s.BoardManufacturer.ToUpperInvariant();
        var site = Fabricantes.FirstOrDefault(f => maker.Contains(f.Marca)).Site;
        if (site is null)
            return null;
        return $"https://www.google.com/search?q={Uri.EscapeDataString($"{s.BoardManufacturer} {product} drivers site:{site}")}";
    }
}
