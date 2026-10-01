using Rkzfps.Core.Model;

namespace Rkzfps.Core.Diagnostics;

/// <summary>As peças que definem "este PC", guardadas a cada análise para notar troca.</summary>
public sealed record HardwareFingerprint
{
    public string Board { get; init; } = "";
    public string Cpu { get; init; } = "";
    public string CpuVendor { get; init; } = "";

    /// <summary>Placa de vídeo que roda o jogo (a dedicada, quando existe).</summary>
    public string Gpu { get; init; } = "";
    public string GpuVendor { get; init; } = "";
    public string SystemDisk { get; init; } = "";
    public IReadOnlyList<string> OtherDisks { get; init; } = [];
    public int RamGb { get; init; }

    public static HardwareFingerprint From(SystemSnapshot s)
    {
        var gpu = s.Gpus.FirstOrDefault(g => !g.LikelyIntegrated) ?? s.Gpus.FirstOrDefault();
        return new HardwareFingerprint
        {
            Board = $"{s.BoardManufacturer} {s.BoardProduct}".Trim(),
            Cpu = s.Cpu?.Name.Trim() ?? "",
            CpuVendor = s.Cpu?.Manufacturer.Trim() ?? "",
            Gpu = gpu?.Name.Trim() ?? "",
            GpuVendor = gpu?.Vendor.ToString() ?? "",
            SystemDisk = s.Disks.FirstOrDefault(d => d.IsSystemDrive)?.Model.Trim() ?? "",
            OtherDisks = s.Disks.Where(d => !d.IsSystemDrive && d.Model.Length > 0).Select(d => d.Model.Trim()).Distinct().Order().ToList(),
            // Arredonda: o Windows reporta a memória utilizável, que varia alguns MB.
            RamGb = s.Memory is { TotalBytes: > 0 } m ? (int)Math.Round(m.TotalBytes / (1024.0 * 1024 * 1024)) : 0,
        };
    }
}

/// <summary>Uma peça que mudou, se isso pede formatação e o que fazer.</summary>
public sealed record HardwareAdvice(string Part, string From, string To, bool FormatRecommended, string Steps);

/// <summary>
/// "Troquei uma peça: preciso formatar?" Resposta pela peça que mudou, sem
/// chute: só placa-mãe nova pede formatação (muda chipset, controladora de
/// disco e rede, e o Windows fica com os drivers da placa antiga). O resto
/// se resolve com driver ou BIOS, e a tela diz qual.
/// </summary>
public static class HardwareAdvisor
{
    public static IReadOnlyList<HardwareAdvice> Compare(HardwareFingerprint? before, HardwareFingerprint now)
    {
        // Sem base (instalação nova, ou a primeira análise depois de formatar): nada a comparar.
        if (before is null)
            return [];

        var list = new List<HardwareAdvice>();
        if (Changed(before.Board, now.Board))
            list.Add(new HardwareAdvice("Placa-mãe", before.Board, now.Board, true,
                "Placa-mãe nova muda chipset, controladora de disco e rede, e o Windows segue com os drivers da placa antiga. " +
                "O caminho limpo é formatar: antes, use \"Preparar formatação\" para salvar seus drivers e confira o backup dos seus arquivos. " +
                "Se não quiser formatar agora, instale ao menos o chipset da placa nova pelo site do fabricante. A troca de placa pode pedir para reativar o Windows."));
        else if (Changed(before.Cpu, now.Cpu))
            list.Add(new HardwareAdvice("Processador", before.Cpu, now.Cpu, false,
                "Processador novo na mesma placa não pede formatação. Confira se a BIOS está na versão que a placa exige para ele " +
                "(botão da placa-mãe na página Drivers e reparo) e instale o chipset mais novo."));

        if (Changed(before.Gpu, now.Gpu))
            list.Add(before.GpuVendor.Length > 0 && !string.Equals(before.GpuVendor, now.GpuVendor, StringComparison.OrdinalIgnoreCase)
                ? new HardwareAdvice("Placa de vídeo", before.Gpu, now.Gpu, false,
                    $"Troca de marca não pede formatação. Desinstale o programa do driver antigo em Configurações > Aplicativos, reinicie " +
                    $"e instale o driver da marca nova (página Drivers e reparo). Sobra de driver de outra marca é causa comum de tela preta e travada.")
                : new HardwareAdvice("Placa de vídeo", before.Gpu, now.Gpu, false,
                    "Mesma marca: não precisa formatar nem remover nada. Atualize o driver pela página Drivers e reparo."));

        if (Changed(before.SystemDisk, now.SystemDisk))
            list.Add(new HardwareAdvice("Disco do Windows", before.SystemDisk, now.SystemDisk, false,
                "O Windows agora roda de outro disco (clonado): não precisa formatar. Confira em Gerenciamento de Disco se o espaço todo do disco novo está em uso."));

        foreach (var disk in now.OtherDisks.Except(before.OtherDisks, StringComparer.OrdinalIgnoreCase))
            list.Add(new HardwareAdvice("Disco novo", "", disk, false,
                "Disco novo não pede formatação do Windows. Se ele não aparecer no Explorador, inicialize e formate só ele no Gerenciamento de Disco. Isso apaga o que estiver nesse disco."));

        if (before.RamGb > 0 && now.RamGb > 0 && before.RamGb != now.RamGb)
            list.Add(new HardwareAdvice("Memória", $"{before.RamGb} GB", $"{now.RamGb} GB", false,
                "Memória nova não pede formatação. Confira na tela Início se ela está na velocidade certa (perfil XMP ou EXPO na BIOS)."));

        return list;
    }

    public static string Title(IReadOnlyList<HardwareAdvice> changes) =>
        changes.Any(c => c.FormatRecommended) ? "Peça nova: formatar é o recomendado"
        : changes.Count == 1 ? $"Peça nova detectada: {changes[0].Part.ToLowerInvariant()}"
        : $"{changes.Count} peças novas detectadas";

    public static string Text(IReadOnlyList<HardwareAdvice> changes) =>
        string.Join(" ", changes.Take(2).Select(c => $"{c.Part}: {c.To}.")) +
        (changes.Any(c => c.FormatRecommended) ? " Clique para ver como preparar." : " Não precisa formatar. Clique para ver o que fazer.");

    private static bool Changed(string a, string b) =>
        a.Length > 0 && b.Length > 0 && !string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
