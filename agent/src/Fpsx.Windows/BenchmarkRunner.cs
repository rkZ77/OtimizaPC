using System.Diagnostics;
using System.Runtime.InteropServices;
using Fpsx.Core.Benchmark;

namespace Fpsx.Windows;

public sealed record BenchmarkRequest(string PresentMonPath, string Process, int DurationSeconds, string OutputCsv);

/// <summary>
/// Roda o PresentMon (ferramenta aberta da Intel) contra o processo do jogo e
/// amostra CPU, GPU e RAM em paralelo. O FPSX não embute nem baixa o
/// PresentMon: o caminho vem do usuário ou da pasta tools do instalador.
/// </summary>
public static class BenchmarkRunner
{
    public static (BenchmarkRun Run, string Log) Capture(BenchmarkRequest request, string label, string gameId, Action<string>? progress = null)
    {
        if (!File.Exists(request.PresentMonPath))
            throw new FileNotFoundException("PresentMon não encontrado. Baixe em github.com/GameTechDev/PresentMon e informe o caminho com --presentmon.", request.PresentMonPath);

        var processName = request.Process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? request.Process[..^4] : request.Process;
        if (System.Diagnostics.Process.GetProcessesByName(processName).Length == 0)
            throw new InvalidOperationException($"{request.Process} não está rodando. Abra o jogo no cenário de teste antes de iniciar a captura.");

        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputCsv)!);
        var psi = new ProcessStartInfo(request.PresentMonPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in new[]
                 {
                     "--process_name", request.Process, "--output_file", request.OutputCsv,
                     "--timed", request.DurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "--terminate_after_timed", "--stop_existing_session",
                 })
            psi.ArgumentList.Add(arg);

        progress?.Invoke($"Capturando {request.DurationSeconds} s de {request.Process}");
        using var sampler = new SystemSampler();
        using var pm = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("Falha ao iniciar o PresentMon.");
        var stdout = pm.StandardOutput.ReadToEndAsync();
        var stderr = pm.StandardError.ReadToEndAsync();
        if (!pm.WaitForExit((request.DurationSeconds + 30) * 1000))
        {
            pm.Kill(entireProcessTree: true);
            throw new TimeoutException("O PresentMon não terminou no tempo esperado.");
        }

        var log = stdout.Result + stderr.Result;
        if (!File.Exists(request.OutputCsv))
            throw new InvalidOperationException("O PresentMon não gerou o CSV. Ele exige executar como administrador ou pertencer ao grupo 'Performance Log Users'.\n" + log.Trim());

        var frametimes = PresentMonCsv.ReadFrametimes(File.ReadAllText(request.OutputCsv), request.Process);
        var samples = sampler.Stop();
        return (new BenchmarkRun
        {
            Id = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..4],
            Label = label,
            GameId = gameId,
            Process = request.Process,
            At = DateTimeOffset.Now,
            Stats = FrameStats.From(frametimes),
            AvgCpuPercent = samples.Cpu,
            AvgGpuPercent = samples.Gpu,
            AvgRamPercent = samples.Ram,
            SourceCsv = request.OutputCsv,
        }, log);
    }
}

/// <summary>Amostra uso de CPU, GPU (engine 3D) e RAM a cada segundo, em background.</summary>
internal sealed class SystemSampler : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _task;
    private readonly List<double> _cpu = [];
    private readonly List<double> _gpu = [];
    private readonly List<double> _ram = [];

    public SystemSampler()
    {
        _task = Task.Run(Loop);
    }

    private async Task Loop()
    {
        while (!_cts.IsCancellationRequested)
        {
            var cpu = Wmi.Query("SELECT PercentProcessorUtility, PercentProcessorTime FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'").FirstOrDefault();
            if (cpu is not null && (cpu.Long("PercentProcessorUtility") ?? cpu.Long("PercentProcessorTime")) is { } c)
                lock (_cpu) _cpu.Add(Math.Min(100, c));

            // Soma do engine 3D de todos os processos, por adaptador; o maior
            // adaptador é o que o jogo usa.
            var engines = Wmi.Query("SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            var byAdapter = engines
                .Where(e => e.Str("Name").EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase))
                .GroupBy(e => Luid(e.Str("Name")))
                .Select(g => g.Sum(e => e.Long("UtilizationPercentage") ?? 0))
                .DefaultIfEmpty(-1)
                .Max();
            if (byAdapter >= 0)
                lock (_gpu) _gpu.Add(Math.Min(100, byAdapter));

            var mem = new Native.MemoryStatusEx { Length = (uint)Marshal.SizeOf<Native.MemoryStatusEx>() };
            if (Native.GlobalMemoryStatusEx(ref mem))
                lock (_ram) _ram.Add(mem.MemoryLoad);

            try
            {
                await Task.Delay(1000, _cts.Token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private static string Luid(string name)
    {
        var i = name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
        var j = name.IndexOf("_phys", StringComparison.OrdinalIgnoreCase);
        return i >= 0 && j > i ? name[i..j] : "";
    }

    public (double? Cpu, double? Gpu, double? Ram) Stop()
    {
        _cts.Cancel();
        try
        {
            _task.Wait(3000);
        }
        catch (AggregateException)
        {
        }

        static double? Avg(List<double> l)
        {
            lock (l)
                return l.Count > 0 ? l.Average() : null;
        }

        return (Avg(_cpu), Avg(_gpu), Avg(_ram));
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
