using System.Globalization;
using System.Text.RegularExpressions;
using Fpsx.Core.Model;
using Fpsx.Core.Optimizations;

namespace Fpsx.Core.Diagnostics;

/// <summary>
/// Velocidade de fábrica do pente, lida do código do fabricante. O Windows só
/// informa a velocidade atual e a padrão (JEDEC), nunca a do perfil XMP/EXPO;
/// o código do pente costuma trazer esse número (CMK16GX4M2B3200C16, F4-3600C18,
/// KF432C16, HX426C16, BL8G32C16). Sem padrão reconhecido, devolve null e a
/// regra não afirma nada.
/// </summary>
public static partial class MemorySpeed
{
    private static readonly int[] Known = [2133, 2400, 2666, 2933, 3000, 3200, 3333, 3466, 3600, 3733, 3866, 4000, 4133, 4266, 4400, 4600, 4800, 5200, 5600, 6000, 6400, 6800, 7200, 7600, 8000];

    [GeneratedRegex(@"(?<!\d)(\d{4})(?!\d)")]
    private static partial Regex FourDigits();

    // Kingston Fury/HyperX (KF432C16, HX426C16) e Crucial Ballistix (BL8G32C16):
    // dois dígitos são a velocidade em centenas.
    [GeneratedRegex(@"(?:KF[3-5]|HX[3-5]|BL\d{1,2}G)(\d{2})C", RegexOptions.IgnoreCase)]
    private static partial Regex TwoDigits();

    public static int? RatedFromPartNumber(string partNumber)
    {
        if (string.IsNullOrWhiteSpace(partNumber))
            return null;
        foreach (Match m in FourDigits().Matches(partNumber))
        {
            var v = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (Known.Contains(v))
                return v;
        }

        var two = TwoDigits().Match(partNumber);
        if (two.Success)
        {
            var v = int.Parse(two.Groups[1].Value, CultureInfo.InvariantCulture) * 100;
            // 2666 aparece como "26", 2933 como "29", 3466 como "34"...
            var match = Known.FirstOrDefault(k => k / 100 == v / 100);
            return match > 0 ? match : null;
        }

        return null;
    }
}

/// <summary>
/// Onde fica o perfil de velocidade da memória na BIOS de cada fabricante.
/// O RKZFPS não grava na BIOS (uma gravação que falha pode impedir o PC de
/// ligar); ele reinicia direto na BIOS e diz exatamente onde clicar.
/// </summary>
public static class BiosGuide
{
    public static string MemoryProfileSteps(string boardManufacturer, bool amd)
    {
        var m = boardManufacturer.ToUpperInvariant();
        var profile = amd ? "EXPO (ou DOCP)" : "XMP";
        if (m.Contains("ASUS"))
            return $"Na BIOS da ASUS: aperte F7 (modo avançado), abra Ai Tweaker, em Ai Overclock Tuner escolha {(amd ? "EXPO I ou D.O.C.P" : "XMP I")} e aperte F10 para salvar.";
        if (m.Contains("MICRO-STAR") || m.Contains("MSI"))
            return $"Na BIOS da MSI: no topo da tela inicial, ligue o botão {(amd ? "A-XMP ou EXPO" : "XMP")} no Profile 1 e aperte F10 para salvar.";
        if (m.Contains("GIGABYTE"))
            return "Na BIOS da Gigabyte: abra a aba Tweaker, em Extreme Memory Profile (X.M.P.) escolha Profile1 e aperte F10 para salvar.";
        if (m.Contains("ASROCK"))
            return "Na BIOS da ASRock: abra OC Tweaker, em DRAM Profile Setting (ou Load XMP Setting) escolha o Profile 1 e aperte F10 para salvar.";
        return $"Na BIOS, procure a opção {profile} ou perfil de memória (costuma ficar em Overclock, Tweaker ou Avançado), escolha o Profile 1 e aperte F10 para salvar.";
    }
}

/// <summary>
/// Montagem do PC que tira FPS sem a pessoa saber, e que nenhum ajuste do
/// Windows resolve: memória abaixo da velocidade de fábrica, um pente só,
/// monitor ligado na placa-mãe e jogo no HD. O RKZFPS não mexe em BIOS nem
/// em cabo: explica o passo, em palavras simples.
/// </summary>
public sealed class HardwareSetupDiagnostic : IDiagnostic
{
    public string Id => "hardware-setup";

    /// <summary>Folga para arredondamentos de firmware (3200 vira 3192, 2666 vira 2667).</summary>
    public const int SpeedSlackMts = 100;

    public IEnumerable<Finding> Run(EvaluationContext context)
    {
        var s = context.Snapshot;
        foreach (var f in new[] { RamSpeed(s), SingleChannel(s), MonitorOnMotherboard(s) }.OfType<Finding>())
            yield return f;
        foreach (var f in GamesOnHdd(s))
            yield return f;
    }

    /// <summary>Botão do app que reinicia o PC direto na BIOS (UEFI).</summary>
    public static FindingAction RebootToBios => new("Reiniciar direto na BIOS", FindingAction.RebootToFirmware);

    private Finding? RamSpeed(SystemSnapshot s)
    {
        // Notebook quase nunca tem a opção: a memória fica na velocidade que o fabricante travou.
        if (s.Power?.HasBattery == true)
            return null;
        var modules = s.Memory?.Modules ?? [];
        var slow = modules
            .Select(m => (Module: m, Rated: MemorySpeed.RatedFromPartNumber(m.PartNumber)))
            .Where(x => x.Rated is { } r && x.Module.ConfiguredMts > 0 && r > x.Module.ConfiguredMts + SpeedSlackMts)
            .ToList();
        if (slow.Count == 0)
            return null;

        var rated = slow.Min(x => x.Rated!.Value);
        var now = slow.Max(x => x.Module.ConfiguredMts);
        var amd = s.Cpu?.Manufacturer.Contains("AMD", StringComparison.OrdinalIgnoreCase) == true;
        return new Finding
        {
            DiagnosticId = Id,
            Area = Areas.Ram,
            Status = HealthStatus.Attention,
            ImpactArea = "FPS",
            Impact = Potential.High,
            Title = $"Memória rodando abaixo da velocidade de fábrica ({now} em vez de {rated})",
            Detail = $"Os pentes foram feitos para {rated}, mas estão a {now}. É o padrão de fábrica da placa-mãe: o perfil de velocidade vem desligado. " +
                     "Memória mais lenta segura o processador, e isso aparece como FPS mais baixo e mais travadas em jogos que pesam na CPU.",
            Recommendation = BiosGuide.MemoryProfileSteps(s.BoardManufacturer, amd) +
                             " É uma opção de fábrica da placa, não é overclock arriscado. Se a opção não aparecer, a placa não permite mudar a velocidade: não é defeito.",
            Actions = [RebootToBios],
            Evidence = new Dictionary<string, string>
            {
                ["velocidade_atual"] = now.ToString(CultureInfo.InvariantCulture),
                ["velocidade_de_fabrica"] = rated.ToString(CultureInfo.InvariantCulture),
                ["pentes"] = string.Join(", ", slow.Select(x => x.Module.PartNumber.Trim())),
            },
        };
    }

    private Finding? SingleChannel(SystemSnapshot s)
    {
        var modules = s.Memory?.Modules ?? [];
        if (modules.Count != 1)
            return null;
        var onlyIntegrated = s.Gpus.Count > 0 && s.Gpus.All(g => g.LikelyIntegrated);
        var gb = modules[0].CapacityBytes / (1024.0 * 1024 * 1024);
        return new Finding
        {
            DiagnosticId = Id,
            Area = Areas.Ram,
            Status = HealthStatus.Attention,
            ImpactArea = "FPS",
            Impact = onlyIntegrated ? Potential.High : Potential.Moderate,
            Title = "Memória com um pente só",
            Detail = $"O PC tem um pente de {gb:0} GB. Com um só, a memória trabalha em canal único, com metade da velocidade de acesso." +
                     (onlyIntegrated ? " Neste PC o vídeo é integrado e usa essa mesma memória, então o efeito no FPS é grande." : ""),
            Recommendation = "Na próxima compra, prefira dois pentes iguais (por exemplo, 2 de 8 GB em vez de 1 de 16 GB), nos encaixes indicados no manual da placa-mãe.",
            Evidence = new Dictionary<string, string> { ["pentes"] = "1", ["encaixe"] = modules[0].Slot },
        };
    }

    private Finding? MonitorOnMotherboard(SystemSnapshot s)
    {
        // Notebook liga o monitor pelo vídeo integrado de propósito (troca de
        // placa automática): lá isso é normal e não é oferecido.
        if (s.Power?.HasBattery != false)
            return null;
        var dedicated = s.Gpus.Where(g => !g.LikelyIntegrated).ToList();
        var integrated = s.Gpus.Where(g => g.LikelyIntegrated).ToList();
        if (dedicated.Count == 0 || integrated.Count == 0)
            return null;
        var primary = s.Displays.FirstOrDefault(d => d.IsPrimary) ?? s.Displays.FirstOrDefault();
        if (primary is null || string.IsNullOrWhiteSpace(primary.AdapterName))
            return null;
        var onIntegrated = integrated.Any(g => string.Equals(g.Name.Trim(), primary.AdapterName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!onIntegrated)
            return null;
        return new Finding
        {
            DiagnosticId = Id,
            Area = Areas.Display,
            Status = HealthStatus.Problem,
            ImpactArea = "FPS",
            Impact = Potential.High,
            Title = "Monitor ligado na placa-mãe, não na placa de vídeo",
            Detail = $"O cabo do monitor está na saída da placa-mãe ({primary.AdapterName}). Assim a imagem passa pelo vídeo integrado, e a {dedicated[0].Name} trabalha menos do que pode.",
            Recommendation = "Com o PC desligado, tire o cabo do monitor da saída da placa-mãe (perto das portas USB de trás) e ligue numa das saídas da placa de vídeo (mais embaixo, na horizontal).",
            Evidence = new Dictionary<string, string> { ["monitor_em"] = primary.AdapterName, ["placa_de_video"] = dedicated[0].Name },
        };
    }

    private IEnumerable<Finding> GamesOnHdd(SystemSnapshot s)
    {
        var ssd = s.Disks.Where(d => d.Media == MediaKind.Ssd).OrderByDescending(d => d.FreeBytes).FirstOrDefault();
        foreach (var game in s.Games)
        {
            if (game.InstallPath.Length < 2)
                continue;
            var letter = game.InstallPath[..2].ToUpperInvariant();
            var disk = s.Disks.FirstOrDefault(d => string.Equals(d.DriveLetter, letter, StringComparison.OrdinalIgnoreCase));
            if (disk?.Media != MediaKind.Hdd)
                continue;
            yield return new Finding
            {
                DiagnosticId = Id,
                Area = Areas.Storage,
                Status = HealthStatus.Attention,
                ImpactArea = "STUTTER",
                Impact = Potential.Moderate,
                Title = $"{game.Name} instalado em HD comum ({letter})",
                Detail = "HD comum demora para entregar texturas e mapas. Isso não baixa o FPS médio, mas causa travadinhas quando o jogo carrega algo novo e deixa o carregamento lento.",
                Recommendation = ssd is not null
                    ? $"Mova o jogo para o SSD ({ssd.DriveLetter}, {ssd.FreeBytes / 1024 / 1024 / 1024} GB livres). Na Steam: Propriedades do jogo > Arquivos instalados > Mover pasta de instalação."
                    : "Se puder, instale o jogo num SSD. É a peça que mais reduz travadinha de carregamento pelo preço.",
                Evidence = new Dictionary<string, string> { ["jogo"] = game.GameId, ["disco"] = letter },
            };
        }
    }
}
