using Rkzfps.Core.Engine;
using Rkzfps.Core.Model;

namespace Rkzfps.Core.Tests;

/// <summary>O que o fim do teste grátis desfaz (Engine.TrialEnd).</summary>
public class TrialEndTests
{
    private static readonly Change Usuario = new RegistryValueChange(RegistryRoot.CurrentUser, "x", "y", RegValue.DWord(1));
    private static readonly Change Sistema = new RegistryValueChange(RegistryRoot.LocalMachine, "x", "y", RegValue.DWord(1));

    private static ChangeRecord C(string id, ChangeStatus status, Change applied, bool reversivel = true) =>
        new() { Id = id, OptimizationId = "opt", Status = status, Applied = applied, Inverse = reversivel ? applied : null };

    private static SessionRecord S(string id, int dia, params ChangeRecord[] changes) =>
        new() { Id = id, StartedAt = new DateTimeOffset(2026, 9, dia, 12, 0, 0, TimeSpan.Zero), Status = SessionStatus.Completed, Changes = changes };

    [Fact]
    public void So_entra_o_que_esta_aplicado_e_tem_estado_anterior()
    {
        var sessoes = new[]
        {
            S("ja-desfeita", 1, C("a", ChangeStatus.RolledBack, Usuario)),
            S("sem-volta", 2, C("b", ChangeStatus.Applied, Usuario, reversivel: false)),
            S("falhou", 3, C("c", ChangeStatus.Failed, Usuario)),
            S("aplicada", 4, C("d", ChangeStatus.Applied, Usuario), C("e", ChangeStatus.RollbackSkipped, Usuario)),
        };

        var pendentes = TrialEnd.Pending(sessoes);

        Assert.Equal(["aplicada"], pendentes.Select(s => s.Id));
        // A alteração que a pessoa mudou depois (RollbackSkipped) não conta nem é forçada.
        Assert.Equal(1, TrialEnd.Count(pendentes));
    }

    [Fact]
    public void Mais_nova_primeiro_e_separa_o_que_precisa_de_administrador()
    {
        var velha = S("velha", 1, C("a", ChangeStatus.Applied, Usuario));
        var nova = S("nova", 9, C("b", ChangeStatus.Applied, Usuario), C("c", ChangeStatus.Applied, Sistema));

        var pendentes = TrialEnd.Pending([velha, nova]);

        Assert.Equal(["nova", "velha"], pendentes.Select(s => s.Id));
        Assert.True(TrialEnd.NeedsAdmin(nova));
        Assert.False(TrialEnd.NeedsAdmin(velha));
        Assert.Equal(3, TrialEnd.Count(pendentes));
    }

    [Fact]
    public void Sem_nada_aplicado_nao_ha_o_que_desfazer()
    {
        Assert.Empty(TrialEnd.Pending([]));
        Assert.Empty(TrialEnd.Pending([S("s", 1, C("a", ChangeStatus.RolledBack, Sistema))]));
    }
}
