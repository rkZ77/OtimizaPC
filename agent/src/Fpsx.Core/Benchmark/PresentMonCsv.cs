using System.Globalization;

namespace Fpsx.Core.Benchmark;

/// <summary>
/// Lê o CSV do PresentMon (1.x: MsBetweenPresents; 2.x: FrameTime ou
/// MsBetweenPresents). O FPSX não reinventa captura de frames: usa a
/// ferramenta aberta da Intel que a indústria usa como referência.
/// </summary>
public static class PresentMonCsv
{
    private static readonly string[] FrametimeColumns = ["MsBetweenPresents", "FrameTime", "msBetweenPresents"];

    public static IReadOnlyList<double> ReadFrametimes(string csv, string? processName = null)
    {
        using var reader = new StringReader(csv);
        var header = reader.ReadLine();
        if (header is null)
            return [];

        var columns = SplitLine(header);
        var ftIndex = FrametimeColumns.Select(c => columns.FindIndex(h => h.Equals(c, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(i => i >= 0, -1);
        if (ftIndex < 0)
            throw new InvalidDataException("CSV sem coluna de frametime (MsBetweenPresents ou FrameTime). Confirme que o arquivo é do PresentMon.");
        var appIndex = columns.FindIndex(h => h.Equals("Application", StringComparison.OrdinalIgnoreCase));

        var result = new List<double>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
                continue;
            var cells = SplitLine(line);
            if (cells.Count <= ftIndex)
                continue;
            if (processName is not null && appIndex >= 0 && appIndex < cells.Count
                && !cells[appIndex].Equals(processName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (double.TryParse(cells[ftIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms))
                result.Add(ms);
        }

        return result;
    }

    /// <summary>
    /// Quadros com o instante em que ocorreram (segundos desde o início da
    /// captura), lidos em fluxo: uma partida longa gera centenas de MB de CSV
    /// e não cabe carregar inteira. Exige o formato 1.x (--v1_metrics).
    /// </summary>
    public static IEnumerable<(double TimeSeconds, double FrametimeMs, string SwapChain)> ReadTimedFrames(TextReader reader, int? processId = null)
    {
        var header = reader.ReadLine();
        if (header is null)
            yield break;
        var columns = SplitLine(header);
        var time = columns.FindIndex(h => h.Equals("TimeInSeconds", StringComparison.OrdinalIgnoreCase));
        var ft = columns.FindIndex(h => h.Equals("msBetweenPresents", StringComparison.OrdinalIgnoreCase));
        var pid = columns.FindIndex(h => h.Equals("ProcessID", StringComparison.OrdinalIgnoreCase));
        var chain = columns.FindIndex(h => h.Equals("SwapChainAddress", StringComparison.OrdinalIgnoreCase));
        if (time < 0 || ft < 0)
            throw new InvalidDataException("CSV sem TimeInSeconds ou msBetweenPresents. A captura automática usa o PresentMon com --v1_metrics.");

        var want = processId?.ToString(CultureInfo.InvariantCulture);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
                continue;
            var cells = SplitLine(line);
            if (cells.Count <= Math.Max(time, ft))
                continue;
            if (want is not null && pid >= 0 && pid < cells.Count && cells[pid] != want)
                continue;
            if (double.TryParse(cells[time], NumberStyles.Float, CultureInfo.InvariantCulture, out var t)
                && double.TryParse(cells[ft], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms))
                yield return (t, ms, chain >= 0 && chain < cells.Count ? cells[chain] : "");
        }
    }

    // CSV do PresentMon não usa aspas com vírgula dentro, exceto em nomes de
    // processo raros. Tratar aspas aqui evita desalinhar colunas nesses casos.
    private static List<string> SplitLine(string line)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var ch in line)
        {
            if (ch == '"')
                quoted = !quoted;
            else if (ch == ',' && !quoted)
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
                current.Append(ch);
        }

        cells.Add(current.ToString());
        return cells;
    }
}
