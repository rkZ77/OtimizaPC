using Fpsx.Core.Engine;
using Fpsx.Core.Model;
using Fpsx.Core.Optimizations;

namespace Fpsx.Core.Tests;

public class MouseAccelerationTests
{
    private static Evaluation Eval(string? mouseSpeed) =>
        new MouseAccelerationOptimization().Evaluate(new EvaluationContext(
            Pc.Healthy() with { Gaming = new GamingFeatures { MouseSpeedValue = mouseSpeed } }, TestData.Games()));

    [Fact]
    public void Aceleracao_ligada_vira_opcional_e_passa_na_SafetyPolicy()
    {
        var e = Eval("1");
        // Preferência de controle: nunca "Recomendado", nunca na seleção automática.
        Assert.Equal(Decision.Optional, e.Decision);
        var changes = e.Proposals.Single().Changes;
        Assert.Equal(3, changes.Count);
        foreach (var c in changes)
            SafetyPolicy.Validate(c);
        Assert.All(changes.Cast<RegistryValueChange>(), c => Assert.Equal("0", c.Value!.Data));
    }

    [Fact]
    public void Ja_desligada_ou_nao_lida_nao_oferece_nada()
    {
        Assert.Equal(Decision.AlreadyOptimal, Eval("0").Decision);
        Assert.Equal(Decision.NotApplicable, Eval(null).Decision);
    }

    [Fact]
    public void SafetyPolicy_barra_valor_fora_do_formato_do_Windows()
    {
        var ok = new RegistryValueChange(RegistryRoot.CurrentUser, RegistryPaths.Mouse, "MouseSpeed", new RegValue(RegistryKind.String, "0"));
        SafetyPolicy.Validate(ok);
        // Rollback para o padrão do Windows também tem que passar.
        SafetyPolicy.Validate(ok with { Name = "MouseThreshold2", Value = new RegValue(RegistryKind.String, "10") });

        Assert.Throws<SafetyViolationException>(() => SafetyPolicy.Validate(ok with { Value = new RegValue(RegistryKind.String, "3") }));
        Assert.Throws<SafetyViolationException>(() => SafetyPolicy.Validate(ok with { Value = RegValue.DWord(0) }));
        Assert.Throws<SafetyViolationException>(() => SafetyPolicy.Validate(ok with { Value = null }));
        Assert.Throws<SafetyViolationException>(() => SafetyPolicy.Validate(ok with { Name = "MouseThreshold1", Value = new RegValue(RegistryKind.String, "99") }));
        Assert.Throws<SafetyViolationException>(() => SafetyPolicy.Validate(ok with { Name = "MouseSensitivity", Value = new RegValue(RegistryKind.String, "10") }));
    }
}
