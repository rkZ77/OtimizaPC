using System.Globalization;

namespace Fpsx.Core.Benchmark;

/// <summary>
/// FPS "agora", para mostrar enquanto a pessoa joga. Recebe as linhas do CSV
/// do PresentMon conforme ele grava e calcula o FPS dos últimos segundos.
///
/// É só para exibir: o número que vale para comparação é o da partida
/// inteira (GameplayAnalyzer), com aquecimento e primeiro plano filtrados.
/// </summary>
public sealed class LiveFpsMeter(int processId, double windowSeconds = 2)
{
    private readonly Queue<(double T, double Ms, string Chain)> _frames = new();
    private int _time = -1, _ft = -1, _pid = -1, _chain = -1;
    private double _last;

    public bool HasHeader => _time >= 0;

    public void Add(string line)
    {
        if (line.Length == 0)
            return;
        var cells = PresentMonCsv.Split(line);
        if (!HasHeader)
        {
            _time = cells.FindIndex(h => h.Equals("TimeInSeconds", StringComparison.OrdinalIgnoreCase));
            _ft = cells.FindIndex(h => h.Equals("msBetweenPresents", StringComparison.OrdinalIgnoreCase));
            _pid = cells.FindIndex(h => h.Equals("ProcessID", StringComparison.OrdinalIgnoreCase));
            _chain = cells.FindIndex(h => h.Equals("SwapChainAddress", StringComparison.OrdinalIgnoreCase));
            return;
        }

        if (_ft < 0 || cells.Count <= Math.Max(_time, _ft))
            return;
        if (_pid >= 0 && _pid < cells.Count && cells[_pid] != processId.ToString(CultureInfo.InvariantCulture))
            return;
        if (!double.TryParse(cells[_time], NumberStyles.Float, CultureInfo.InvariantCulture, out var t)
            || !double.TryParse(cells[_ft], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) || ms <= 0)
            return;
        _last = Math.Max(_last, t);
        _frames.Enqueue((t, ms, _chain >= 0 && _chain < cells.Count ? cells[_chain] : ""));
        while (_frames.Count > 0 && _frames.Peek().T < _last - windowSeconds)
            _frames.Dequeue();
    }

    /// <summary>FPS da janela recente, só da cadeia principal. null = ainda sem quadros.</summary>
    public double? Current
    {
        get
        {
            var main = _frames.GroupBy(f => f.Chain).OrderByDescending(g => g.Count()).FirstOrDefault();
            if (main is null)
                return null;
            var total = main.Sum(f => f.Ms);
            return total <= 0 ? null : main.Count() * 1000.0 / total;
        }
    }

    /// <summary>FPS do pior quadro da janela recente (o pico para baixo do gráfico ao vivo). Pausa acima de 1 s não conta.</summary>
    public double? Low
    {
        get
        {
            var main = _frames.GroupBy(f => f.Chain).OrderByDescending(g => g.Count()).FirstOrDefault();
            var worst = main?.Where(f => f.Ms <= GameplayAnalyzer.PauseFrametimeMs).Select(f => f.Ms).DefaultIfEmpty(0).Max() ?? 0;
            return worst <= 0 ? null : 1000.0 / worst;
        }
    }
}
