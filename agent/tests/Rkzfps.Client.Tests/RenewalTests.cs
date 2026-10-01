namespace Rkzfps.Client.Tests;

public class RenewalTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private static LicenseState Plano(string status, double dias) =>
        new() { Plan = "pro", Status = status, Email = "x@y.z", ExpiresAt = Agora.AddDays(dias) };

    [Fact]
    public void Avisa_so_nos_ultimos_tres_dias_de_plano_ou_teste()
    {
        Assert.False(Plano("active", 10).EndingSoon(Agora));
        Assert.True(Plano("active", 3).EndingSoon(Agora));
        Assert.True(Plano("trial", 0.5).EndingSoon(Agora));
        Assert.Equal(1, Plano("trial", 0.5).DaysLeft(Agora));
        Assert.Equal(0, Plano("active", -2).DaysLeft(Agora));
        // Free e plano sem vencimento nao recebem aviso.
        Assert.False(LicenseState.Free().EndingSoon(Agora));
        Assert.False((Plano("active", 1) with { ExpiresAt = null }).EndingSoon(Agora));
    }

    [Fact]
    public void Vencido_nao_e_aviso_de_bandeja_e_sim_cartao()
    {
        var vencido = new LicenseState { Status = "expired", Email = "x@y.z" };
        Assert.True(vencido.Expired);
        Assert.False(vencido.EndingSoon(Agora));
        // O plano efetivo continua Free: nada pago liberado.
        Assert.Equal("free", vencido.Plan);
    }
}
