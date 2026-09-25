using Fpsx.Core.Model;

namespace Fpsx.Core.Optimizations;

// Ferramentas de TROUBLESHOOTING: resolvem um problema específico e nunca são
// apresentadas como ganho de FPS. Sempre Optional, sempre por pedido do usuário.

public sealed class DirectXShaderCacheClear : IOptimization
{
    public string Id => "shader-cache-clear-directx";

    public Evaluation Evaluate(EvaluationContext context)
    {
        var changes = new List<Change> { new CacheClearChange(CacheTarget.DirectXShaderCache) };
        var vendors = context.Snapshot.Gpus.Select(g => g.Vendor).ToHashSet();
        if (vendors.Contains(GpuVendor.Nvidia))
            changes.Add(new CacheClearChange(CacheTarget.NvidiaDxCache));
        if (vendors.Contains(GpuVendor.Amd))
            changes.Add(new CacheClearChange(CacheTarget.AmdDxCache));

        return new Evaluation
        {
            Decision = Decision.Optional,
            Potential = Potential.None,
            Reason = "Útil quando há artefatos gráficos, travamentos ou stutter anormal depois de atualizar driver ou jogo. Não aumenta FPS.",
            Warning = "Os jogos vão recompilar os shaders: as primeiras partidas depois da limpeza podem ter MAIS stutter até o cache se refazer.",
            Proposals = [new Proposal(Id, "Limpar caches de shader do sistema e do driver", changes, Potential.None, "Troubleshooting.")],
        };
    }
}

public sealed class Cs2ShaderCacheClear : IOptimization
{
    public string Id => "shader-cache-clear-cs2";

    public Evaluation Evaluate(EvaluationContext context)
    {
        if (context.Game("cs2") is null)
            return Evaluation.NotApplicable("CS2 não foi encontrado neste PC.");

        return new Evaluation
        {
            Decision = Decision.Optional,
            Potential = Potential.None,
            Reason = "Útil quando o CS2 apresenta stutter anormal ou erros gráficos depois de uma atualização. Não aumenta FPS.",
            Warning = "A Steam vai baixar ou recompilar o cache de shaders do CS2. As primeiras partidas podem ter mais stutter.",
            Proposals = [new Proposal(Id, "Limpar o cache de shaders do CS2 na Steam", [new CacheClearChange(CacheTarget.SteamShaderCacheCs2)], Potential.None, "Troubleshooting.")],
        };
    }
}

public sealed class DnsFlush : IOptimization
{
    public string Id => "dns-flush";

    public Evaluation Evaluate(EvaluationContext context) => new()
    {
        Decision = Decision.Optional,
        Potential = Potential.None,
        Reason = "Resolve sites ou serviços que não abrem por um registro de DNS antigo em cache. Não reduz ping.",
        Proposals = [new Proposal(Id, "Limpar o cache de DNS", [new NetworkRepairChange(NetworkRepairKind.FlushDns)], Potential.None, "Troubleshooting de rede.")],
    };
}

public sealed class WinsockReset : IOptimization
{
    public string Id => "winsock-reset";

    public Evaluation Evaluate(EvaluationContext context) => new()
    {
        Decision = Decision.Optional,
        Potential = Potential.None,
        Reason = "Repara a pilha de rede quando programas perdem conexão, geralmente depois de remover VPN, antivírus ou proxy. Não deixa a internet mais rápida.",
        Warning = "Exige reinício. Alguns softwares de VPN ou firewall de terceiros podem precisar ser reinstalados.",
        Proposals = [new Proposal(Id, "Redefinir o catálogo Winsock", [new NetworkRepairChange(NetworkRepairKind.WinsockReset)], Potential.None, "Troubleshooting de rede.")],
    };
}
