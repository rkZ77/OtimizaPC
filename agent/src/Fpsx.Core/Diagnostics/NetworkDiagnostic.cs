using Fpsx.Core.Model;
using Fpsx.Core.Optimizations;

namespace Fpsx.Core.Diagnostics;

/// <summary>
/// Separa latência, jitter e perda, e separa rede local de internet: perda já
/// no roteador (gateway) é Wi-Fi ou cabo, não é "DNS lento". Nenhum resultado
/// daqui é vendido como FPS.
/// </summary>
public sealed class NetworkDiagnostic : IDiagnostic
{
    public string Id => "network-quality";

    public IEnumerable<Finding> Run(EvaluationContext context)
    {
        var n = context.Snapshot.Network;
        if (n is null || n.AdapterType == "NONE")
        {
            yield return new Finding { DiagnosticId = Id, Area = Areas.Network, Status = HealthStatus.Unknown, Title = "Rede não testada", Detail = "Nenhum adaptador de rede ativo com gateway." };
            yield break;
        }

        var evidence = new Dictionary<string, string>
        {
            ["adaptador"] = $"{n.AdapterName} ({n.AdapterType})",
            ["velocidade_link"] = n.LinkSpeedMbps is { } sp ? $"{sp} Mbps" : "desconhecida",
        };
        foreach (var p in n.Pings)
            evidence[$"{p.Role} {p.Target}"] = p.Received == 0
                ? $"sem resposta ({p.Sent} enviados)"
                : $"média {Fmt.Num(p.AvgMs ?? 0)} ms, jitter {Fmt.Num(p.JitterMs ?? 0)} ms, perda {Fmt.Pct(p.LossPercent)}";

        var gateway = n.Pings.FirstOrDefault(p => p.Role == "gateway");
        var internet = n.Pings.Where(p => p.Role == "internet" && p.Sent > 0).ToList();
        var found = false;

        if (gateway is { Sent: > 0 } && gateway.LossPercent >= 2)
        {
            found = true;
            yield return new Finding
            {
                DiagnosticId = Id, Area = Areas.Network, Status = HealthStatus.Problem, ImpactArea = "NETWORK",
                Title = $"Perda de pacotes na rede local: {Fmt.Pct(gateway.LossPercent)}",
                Detail = "Pacotes se perdem antes de chegar ao roteador. Isso causa rubber banding e comandos perdidos, e o problema está entre o PC e o roteador.",
                Recommendation = n.AdapterType == "WIFI"
                    ? "Aproxime o PC do roteador, troque para a banda de 5 GHz ou use cabo de rede."
                    : "Verifique o cabo de rede e a porta do roteador.",
                Evidence = evidence,
            };
        }
        else if (internet.Count > 0 && internet.Average(p => p.LossPercent) >= 2)
        {
            found = true;
            var loss = internet.Average(p => p.LossPercent);
            yield return new Finding
            {
                DiagnosticId = Id, Area = Areas.Network, Status = HealthStatus.Problem, ImpactArea = "NETWORK",
                Title = $"Perda de pacotes na internet: {Fmt.Pct(loss)}",
                Detail = "A rede local está limpa, mas há perda depois do roteador. Isso causa rubber banding e comandos perdidos.",
                Recommendation = "O problema está no provedor ou no modem. Registre o resultado e acione o suporte do provedor. Trocar DNS não resolve perda de pacotes.",
                Evidence = evidence,
            };
        }

        var jitterSource = gateway is { Received: > 0 } && gateway.JitterMs >= 5 ? gateway : internet.Where(p => p.Received > 0).MaxBy(p => p.JitterMs ?? 0);
        if (jitterSource?.JitterMs is { } jitter && jitter >= 10)
        {
            found = true;
            yield return new Finding
            {
                DiagnosticId = Id, Area = Areas.Network, Status = HealthStatus.Attention, ImpactArea = "NETWORK",
                Title = $"Jitter alto: {Fmt.Num(jitter)} ms",
                Detail = jitterSource.Role == "gateway"
                    ? "A latência até o roteador varia muito. Em jogo isso aparece como ping instável."
                    : "A latência até a internet varia muito. Em jogo isso aparece como ping instável.",
                Recommendation = n.AdapterType == "WIFI"
                    ? "Wi-Fi é a causa mais comum de jitter. Cabo de rede resolve na maioria dos casos."
                    : "Verifique se há downloads ou streaming em outros aparelhos da mesma rede durante o jogo.",
                Evidence = evidence,
            };
        }

        if (!found)
        {
            yield return new Finding
            {
                DiagnosticId = Id, Area = Areas.Network, Status = n.AdapterType == "WIFI" ? HealthStatus.Info : HealthStatus.Ok,
                Title = "Rede estável no teste",
                Detail = n.AdapterType == "WIFI"
                    ? "Sem perda nem jitter relevantes no teste. Você está em Wi-Fi, que costuma oscilar mais que cabo em horários de pico."
                    : "Sem perda nem jitter relevantes no teste.",
                Evidence = evidence,
            };
        }
    }
}
