using System.Text;
using Fpsx.Core.Benchmark;

namespace Fpsx.Core.Tests;

public class GameplayReportsTests
{
    // Partida de ~10 min a `fps`, com leituras de GPU no limite e núcleos folgados (diagnóstico: GPU).
    private static GameplaySession Match(string id, double fps, DateTimeOffset at, bool gpuBound = true, string game = "Counter-Strike 2")
    {
        var ms = 1000 / fps;
        return new GameplaySession
        {
            Id = id, GameId = "cs2", GameName = game, StartedAt = at, EndedAt = at.AddMinutes(10),
            MeasuredSeconds = 600,
            Stats = FrameStats.From(Enumerable.Repeat(ms, (int)(600 * fps)).ToList()),
            DisplayHz = 240,
            Timeline = Enumerable.Range(0, 600).Select(t => new FpsPoint(t, fps - 20 + t % 40, fps / 2)).ToList(),
            Load = Enumerable.Range(1, 120).Select(i => new LoadSample(i * 5, 40, gpuBound ? 99 : 60, null, 0) { Foreground = true, CpuMaxCore = 60 }).ToList(),
        };
    }

    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 20, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void Resumo_da_partida_traz_fps_meta_e_o_que_limitou()
    {
        var (title, text) = PostMatchSummary.For(Match("a", 150, T0), goal: 144);
        Assert.Equal("Counter-Strike 2: partida registrada", title);
        Assert.Contains("FPS médio 150", text);
        Assert.Contains("Bateu a meta de 144 FPS", text);
        Assert.Contains("A placa de vídeo segurou o FPS", text);

        var (_, below) = PostMatchSummary.For(Match("b", 120, T0), goal: 144);
        Assert.Contains("Ficou abaixo da meta de 144 FPS", below);
    }

    [Fact]
    public void Resumo_sem_dados_nao_inventa_culpado()
    {
        var old = Match("a", 150, T0) with { Load = [] };
        var (_, text) = PostMatchSummary.For(old);
        Assert.DoesNotContain("segurou", text);
        Assert.DoesNotContain("meta", text);
    }

    [Fact]
    public void Meta_conta_partidas_batidas_e_aponta_o_que_mais_impediu()
    {
        var sessions = new[]
        {
            Match("a", 160, T0),
            Match("b", 120, T0.AddDays(1)),
            Match("c", 110, T0.AddDays(2)),
            Match("d", 130, T0.AddDays(3), gpuBound: false),
        };
        var r = FpsGoal.Evaluate(sessions, 144);
        Assert.Equal(4, r.Matches);
        Assert.Equal(1, r.Hits);
        Assert.Equal(LimitKind.Gpu, r.MainLimit);
        Assert.Equal(2, r.MainLimitCount);
        Assert.Contains("batida em 1 de 4", r.Text);
        Assert.Contains("a placa de vídeo segurou o FPS (2 de 3)", r.Text);
    }

    [Fact]
    public void Meta_sem_partida_e_meta_toda_batida()
    {
        Assert.Contains("Jogue com o RKZFPS aberto", FpsGoal.Evaluate([], 144).Text);
        var all = FpsGoal.Evaluate([Match("a", 200, T0)], 144);
        Assert.Equal(1, all.Hits);
        Assert.DoesNotContain("abaixo", all.Text);
    }

    [Fact]
    public void Meta_so_vale_as_partidas_recentes()
    {
        var many = Enumerable.Range(0, 30).Select(i => Match($"m{i}", i < 10 ? 200 : 100, T0.AddDays(i))).ToList();
        var r = FpsGoal.Evaluate(many, 144);
        Assert.Equal(FpsGoal.Recent, r.Matches);
        // As 10 mais antigas (as que batiam) ficaram de fora.
        Assert.Equal(0, r.Hits);
    }

    [Theory]
    [InlineData("144", 144)]
    [InlineData(" 60 ", 60)]
    [InlineData("", null)]
    [InlineData("abc", null)]
    [InlineData("5", null)]
    [InlineData("99999", null)]
    [InlineData("144,5", null)]
    public void Meta_digitada_so_aceita_inteiro_na_faixa(string text, int? expected) =>
        Assert.Equal(expected, FpsGoal.Parse(text));

    [Fact]
    public void Planilha_no_formato_do_excel_brasileiro()
    {
        var s = Match("a", 187.44, T0) with { GameName = "Jogo; com \"aspas\"", ScreenWidth = 1920, ScreenHeight = 1080 };
        var csv = GameplayCsv.Write([s]);
        var lines = csv.TrimEnd().Split(Environment.NewLine);

        Assert.Equal(2, lines.Length);
        Assert.StartsWith("Data;Jogo;Minutos medidos;FPS médio", lines[0]);
        Assert.Contains("30/09/2026", lines[1]);
        Assert.Contains("\"Jogo; com \"\"aspas\"\"\"", lines[1]);
        Assert.Contains(";187,4;", lines[1]);
        Assert.Contains(";1920x1080;", lines[1]);
        Assert.Contains("GPU LIMITANDO", lines[1]);
        Assert.Equal(GameplayCsv.Header.Length, lines[1].Replace("\"Jogo; com \"\"aspas\"\"\"", "X").Split(';').Length);
    }

    [Fact]
    public void Planilha_deixa_vazio_o_que_nao_foi_lido()
    {
        var old = Match("a", 150, T0) with { Load = [], AvgCpuPercent = null, AvgGpuPercent = null, DisplayHz = null };
        var cells = GameplayCsv.Write([old]).TrimEnd().Split(Environment.NewLine)[1].Split(';');
        var col = (string name) => cells[Array.IndexOf(GameplayCsv.Header, name)];
        Assert.Equal("", col("CPU média (%)"));
        Assert.Equal("", col("Temperatura GPU média (°C)"));
        Assert.Equal("", col("Monitor (Hz)"));
        Assert.Equal("DADOS INSUFICIENTES", col("O que limitou"));
    }

    [Fact]
    public void Planilha_sai_com_bom_para_o_excel_ler_os_acentos()
    {
        var path = Path.Combine(TestData.TempDir(), "partidas.csv");
        GameplayCsv.Save(path, [Match("a", 150, T0)]);
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes is [0xEF, 0xBB, 0xBF, ..]);
        Assert.Contains("Núcleo mais usado", Encoding.UTF8.GetString(bytes));
    }
}
