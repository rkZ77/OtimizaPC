using Rkzfps.Core.Catalog;
using Rkzfps.Core.Engine;
using Rkzfps.Core.Model;
using Rkzfps.Core.Optimizations;

namespace Rkzfps.Core.Tests;

public class BoostStateTests
{
    private static readonly Change Reg = new RegistryValueChange(RegistryRoot.CurrentUser, "x", "y", RegValue.DWord(1));

    private static OptimizationResult R(Decision d, string category = "gaming", params Change[][] proposals) => new()
    {
        Definition = new OptimizationDefinition { Id = "opt", Category = category },
        Decision = d,
        Evaluation = new Evaluation
        {
            Proposals = proposals.Select((c, i) => new Proposal($"opt:{i}", "t", c, Potential.Low, "")).ToList(),
        },
    };

    private static SessionRecord Aplicada(ChangeStatus status, Change? inverse) => new()
    {
        Id = "s1", Status = SessionStatus.Completed,
        Changes = [new ChangeRecord { Id = "c1", OptimizationId = "opt", Status = status, Applied = Reg, Inverse = inverse }],
    };

    [Fact]
    public void Aplicada_e_reversivel_fica_ligada_e_sabe_o_que_desfazer()
    {
        var b = BoostState.For(R(Decision.AlreadyOptimal), [Aplicada(ChangeStatus.Applied, Reg)]);
        Assert.Equal(BoostKind.On, b.Kind);
        Assert.Equal(new ActiveChange("s1", "c1"), b.Active.Single());
        // Desfeita ou sem volta nao conta como ligada.
        Assert.Equal(BoostKind.AlreadyRight, BoostState.For(R(Decision.AlreadyOptimal), [Aplicada(ChangeStatus.RolledBack, Reg)]).Kind);
        Assert.Equal(BoostKind.AlreadyRight, BoostState.For(R(Decision.AlreadyOptimal), [Aplicada(ChangeStatus.Applied, null)]).Kind);
    }

    [Fact]
    public void Cada_decisao_vira_o_controle_certo()
    {
        Assert.Equal(BoostKind.Off, BoostState.For(R(Decision.Recommended, proposals: [Reg]), []).Kind);
        Assert.Equal("opt:0", BoostState.For(R(Decision.Optional, proposals: [Reg]), []).ProposalId);
        Assert.Equal(BoostKind.Pick, BoostState.For(R(Decision.Optional, proposals: [[Reg], [Reg]]), []).Kind);
        Assert.Equal(BoostKind.Locked, BoostState.For(R(Decision.Blocked, proposals: [Reg]), []).Kind);
        Assert.Equal(BoostKind.NotApplicable, BoostState.For(R(Decision.NotApplicable), []).Kind);
    }

    [Fact]
    public void Sem_volta_vira_botao_e_reparo_fica_separado()
    {
        // Fechar programa nao tem desfazer: chave desligada enganaria.
        var fechar = new ProcessCloseChange(10, "chrome");
        Assert.Equal(BoostKind.Action, BoostState.For(R(Decision.Recommended, proposals: [fechar]), []).Kind);
        Assert.Equal(BoostKind.Repair, BoostState.For(R(Decision.Optional, "troubleshooting", [new CacheClearChange(CacheTarget.UserTemp)]), []).Kind);
        // Reparo aplicado continua botao, nao vira chave ligada.
        Assert.Equal(BoostKind.Repair, BoostState.For(R(Decision.Optional, "network", [Reg]), [Aplicada(ChangeStatus.Applied, Reg)]).Kind);
    }
}
