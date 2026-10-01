using Rkzfps.Core.Benchmark;
using Rkzfps.Core.Engine;
using Rkzfps.Core.Model;

namespace Rkzfps.Core.Tests;

public class PlanRecapTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 20, 0, 0, TimeSpan.FromHours(-3));

    private static GameplaySession Match(DateTimeOffset at, double fps)
    {
        var ft = Enumerable.Repeat(1000.0 / fps, (int)(fps * 600)).ToList();
        return GameplayAnalyzer.Build(ft, "cs2", "Counter-Strike 2", at, at.AddMinutes(10), null, null, 240, "0.4.9").Session!;
    }

    private static SessionRecord Session(DateTimeOffset at, params (string Id, ChangeStatus Status, Change Change)[] changes) => new()
    {
        Id = at.ToUnixTimeSeconds().ToString(), Status = SessionStatus.Completed, StartedAt = at,
        Changes = changes.Select(c => new ChangeRecord { OptimizationId = c.Id, Status = c.Status, Applied = c.Change, At = at }).ToList(),
    };

    private static readonly Change Registro = new RegistryValueChange(RegistryRoot.CurrentUser, "x", "y", RegValue.DWord(1));

    private static string? Nome(string id) => id switch { "game-mode-enable" => "Windows Game Mode", "power" => "Plano de energia", _ => null };

    [Fact]
    public void Conta_so_correcoes_que_seguem_no_PC()
    {
        var history = new[]
        {
            Session(T0.AddDays(1), ("game-mode-enable", ChangeStatus.Applied, Registro), ("power", ChangeStatus.RolledBack, Registro)),
            // Limpar cache nao "fica" no PC: nao e' correcao mantida.
            Session(T0.AddDays(2), ("shader-cache-clear-directx", ChangeStatus.Applied, new CacheClearChange(CacheTarget.DirectXShaderCache))),
        };
        var recap = PlanRecap.Build(history, [], Nome);
        Assert.Equal(1, recap.ActiveFixes);
        Assert.Equal(["Windows Game Mode"], recap.FixNames);
        Assert.Equal("1 correção ativa no seu PC: Windows Game Mode.", recap.Lines().Single());
    }

    [Fact]
    public void Ganho_so_entra_quando_a_medicao_passa_do_ruido()
    {
        var pivot = T0.AddDays(1);
        var history = new[] { Session(pivot, ("game-mode-enable", ChangeStatus.Applied, Registro)) };

        var melhorou = new[] { Match(T0, 150), Match(T0.AddHours(1), 152), Match(pivot.AddHours(1), 181), Match(pivot.AddHours(2), 180) };
        var gain = PlanRecap.Build(history, melhorou, Nome).Gains.Single(g => g.Metric == "FPS médio");
        Assert.Equal("Counter-Strike 2", gain.GameName);
        Assert.True(gain.After > gain.Before);
        Assert.Contains(PlanRecap.Build(history, melhorou, Nome).Lines(), l => l.StartsWith("Counter-Strike 2: FPS médio de 151 para 181"));

        // Variacao normal entre partidas: nada de numero inventado.
        var ruido = new[] { Match(T0, 150), Match(T0.AddHours(1), 170), Match(pivot.AddHours(1), 155), Match(pivot.AddHours(2), 168) };
        Assert.Empty(PlanRecap.Build(history, ruido, Nome).Gains);
    }

    [Fact]
    public void Sem_nada_feito_nao_ha_resumo()
    {
        var recap = PlanRecap.Build([], [Match(T0, 150)], Nome);
        Assert.False(recap.HasAnything);
        Assert.Empty(recap.Lines());
    }
}
