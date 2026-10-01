using System.Diagnostics;
using System.Runtime.InteropServices;
using Rkzfps.Core.Benchmark;

namespace Rkzfps.Windows;

/// <summary>
/// Lê o estado do PC durante a partida: uso total e por núcleo, clock,
/// limite de desempenho, GPU (uso, temperatura, clock, VRAM), RAM, falta de
/// página e disco. Tudo de contadores do próprio Windows e do driver de
/// vídeo (a mesma fonte do Gerenciador de Tarefas), sem programa extra.
///
/// É chamado a cada 5 s pelo monitor da partida, com o RKZFPS em prioridade
/// abaixo do normal. Leitura que falha vira null e a tela diz "não disponível":
/// nunca um número inventado.
/// </summary>
internal sealed class GameHealthSampler(int gamePid, string? preferredLuid = null) : IDisposable
{
    private readonly List<double> _cpu = [];
    private readonly List<double> _gpu = [];
    private Dictionary<int, TimeSpan> _threads = [];
    private DateTime _threadsAt;
    private int _threadDenied;
    private GpuKmt? _kmt;
    private string _kmtLuid = "";
    private int _acpiMisses;

    /// <summary>Última leitura, para o painel ao vivo.</summary>
    public LoadSample? Latest { get; private set; }

    public LoadSample Sample(int t, bool foreground, string? app, double appCpu)
    {
        var cpu = CounterMath.Cpu(Wmi.Query(
                "SELECT Name, PercentProcessorTime, PercentProcessorUtility, PercentProcessorPerformance, PercentPerformanceLimit, ProcessorFrequency FROM Win32_PerfFormattedData_Counters_ProcessorInformation")
            .Select(r => new CpuCounterRow(r.Str("Name"), r.Long("PercentProcessorTime"), r.Long("PercentProcessorUtility"),
                r.Long("PercentProcessorPerformance"), r.Long("PercentPerformanceLimit"), r.Long("ProcessorFrequency")))
            .ToList());

        var (gpu, luid) = Gpu3d();
        var (dedicated, shared) = GpuMemory(luid);
        var (temp, clockMhz, clockPct) = GpuSensors(luid);

        double? ram = null;
        var mem = new Native.MemoryStatusEx { Length = (uint)Marshal.SizeOf<Native.MemoryStatusEx>() };
        if (Native.GlobalMemoryStatusEx(ref mem))
            ram = mem.MemoryLoad;

        double? faults = Wmi.Query("SELECT PagesInputPersec FROM Win32_PerfFormattedData_PerfOS_Memory").FirstOrDefault()?.Long("PagesInputPersec");

        // Disco mais ocupado, não a média: com dois discos, o do jogo a 100%
        // apareceria como 50% no total.
        var idle = Wmi.Query("SELECT Name, PercentIdleTime FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk")
            .Where(r => r.Str("Name") != "_Total")
            .Select(r => r.Long("PercentIdleTime"))
            .OfType<long>()
            .ToList();
        double? disk = idle.Count > 0 ? Math.Clamp(100 - idle.Min(), 0, 100) : null;

        if (cpu.Total is { } c)
            lock (_cpu) _cpu.Add(c);
        if (gpu is { } g)
            lock (_gpu) _gpu.Add(g);

        var sample = new LoadSample(t, cpu.Total, gpu, app, appCpu)
        {
            Foreground = foreground,
            CpuMaxCore = cpu.MaxCore,
            GameThreadMax = BusiestGameThread(),
            CpuClockPercent = cpu.ClockPercent,
            CpuClockMhz = cpu.ClockMhz,
            CpuPerfLimit = cpu.PerfLimit,
            CpuTempC = AcpiTemperature(),
            GpuTempC = temp,
            GpuClockMhz = clockMhz,
            GpuClockPercent = clockPct,
            VramUsedMb = dedicated,
            SharedGpuMb = shared,
            RamPercent = ram,
            HardFaultsPerSec = faults,
            DiskActivePercent = disk,
        };
        Latest = sample;
        return sample;
    }

    public (double? Cpu, double? Gpu) Averages()
    {
        static double? Avg(List<double> l)
        {
            lock (l)
                return l.Count > 0 ? l.Average() : null;
        }

        return (Avg(_cpu), Avg(_gpu));
    }

    private readonly Dictionary<string, double> _gameGpu = [];
    private int _gameGpuSamples;

    /// <summary>Uso 3D do processo do jogo somado por placa, e em quantas leituras ele usou alguma GPU.</summary>
    public (IReadOnlyDictionary<string, double> ByLuid, int Samples) GameGpuLoad()
    {
        lock (_gameGpu)
            return (new Dictionary<string, double>(_gameGpu), _gameGpuSamples);
    }

    /// <summary>
    /// Motor 3D por adaptador. A mesma consulta separa o uso do processo do
    /// jogo por placa ("pid_1234_luid_..."): é daí que sai a GPU que o jogo
    /// usou, sem depender do nome nem da ordem das placas. As métricas de GPU
    /// seguem a placa do jogo; sem uso dele na leitura, a mais usada do PC.
    /// </summary>
    private (double? Util, string Luid) Gpu3d()
    {
        var engines = Wmi.Query("SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine")
            .Where(e => e.Str("Name").EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var byAdapter = engines
            .GroupBy(e => CounterMath.LuidKey(e.Str("Name")))
            .ToDictionary(g => g.Key, g => (double)g.Sum(e => e.Long("UtilizationPercentage") ?? 0));
        var prefix = $"pid_{gamePid}_";
        var game = engines
            .Where(e => e.Str("Name").StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => CounterMath.LuidKey(e.Str("Name")))
            .Select(g => (Luid: g.Key, Util: (double)g.Sum(e => e.Long("UtilizationPercentage") ?? 0)))
            .Where(x => x.Luid.Length > 0 && x.Util > 0)
            .ToList();
        if (game.Count > 0)
        {
            lock (_gameGpu)
            {
                _gameGpuSamples++;
                foreach (var (luid, util) in game)
                    _gameGpu[luid] = _gameGpu.GetValueOrDefault(luid) + Math.Min(100, util);
            }
        }

        // Placa a seguir: a do jogo nesta leitura; senão a última em que ele
        // rodou; senão a mais usada; com tudo parado, a de alto desempenho (no
        // empate a zero, a placa virtual do Windows poderia ganhar).
        _lastGameLuid = game.OrderByDescending(x => x.Util).Select(x => x.Luid).FirstOrDefault() ?? _lastGameLuid;
        if (_lastGameLuid is { } g && byAdapter.TryGetValue(g, out var gameAdapter))
            return (Math.Min(100, gameAdapter), g);
        var busy = byAdapter.Where(kv => kv.Key.Length > 0 && kv.Value > 0).OrderByDescending(kv => kv.Value).FirstOrDefault();
        if (busy.Key is not null)
            return (Math.Min(100, busy.Value), busy.Key);
        if (preferredLuid is { } p && byAdapter.TryGetValue(p, out var idle))
            return (Math.Min(100, idle), p);
        return byAdapter.Count == 0 ? (null, "") : (0, byAdapter.Keys.FirstOrDefault(k => k.Length > 0) ?? "");
    }

    private string? _lastGameLuid;

    /// <summary>Uso de 3D de um processo (para reconhecer jogo sem perfil). null = contador indisponível.</summary>
    public static double? Gpu3dOf(int pid)
    {
        var rows = Wmi.Query($"SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine WHERE Name LIKE 'pid_{pid}_%'");
        if (rows.Count == 0)
            return null;
        return Math.Min(100, rows.Where(e => e.Str("Name").EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase)).Sum(e => e.Long("UtilizationPercentage") ?? 0));
    }

    private static (double? Dedicated, double? Shared) GpuMemory(string luid)
    {
        if (luid.Length == 0)
            return (null, null);
        var row = Wmi.Query("SELECT Name, DedicatedUsage, SharedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory")
            .FirstOrDefault(r => CounterMath.LuidKey(r.Str("Name")) == luid);
        const double mb = 1024.0 * 1024;
        return row is null ? (null, null) : (row.Long("DedicatedUsage") / mb, row.Long("SharedUsage") / mb);
    }

    private (double? Temp, double? ClockMhz, double? ClockPct) GpuSensors(string luid)
    {
        if (luid.Length == 0)
            return (null, null, null);
        if (_kmtLuid != luid)
        {
            // O jogo pode trocar de adaptador (notebook com duas GPUs): reabre no certo.
            _kmt?.Dispose();
            _kmt = GpuKmt.Open(luid);
            _kmtLuid = luid;
        }

        return _kmt?.Read() ?? (null, null, null);
    }

    /// <summary>
    /// Thread do jogo mais ocupada. Jogo com anti-cheat nega a leitura: depois
    /// de 3 recusas seguidas o sampler desiste, para não gastar à toa.
    /// </summary>
    private double? BusiestGameThread()
    {
        if (_threadDenied >= 3)
            return null;
        var now = DateTime.UtcNow;
        var current = new Dictionary<int, TimeSpan>();
        try
        {
            using var p = Process.GetProcessById(gamePid);
            foreach (ProcessThread th in p.Threads)
            {
                using (th)
                {
                    try
                    {
                        current[th.Id] = th.TotalProcessorTime;
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                    {
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }

        _threadDenied = current.Count == 0 ? _threadDenied + 1 : 0;
        var result = CounterMath.BusiestThread(_threads, current, (now - _threadsAt).TotalSeconds);
        _threads = current;
        _threadsAt = now;
        return result;
    }

    private double? AcpiTemperature()
    {
        // Sem administrador ou em placa que não informa, a consulta volta vazia
        // sempre: depois de 3 tentativas, não pergunta mais nesta partida.
        if (_acpiMisses >= 3)
            return null;
        var t = SnapshotCollector.AcpiTemperature();
        _acpiMisses = t is null ? _acpiMisses + 1 : 0;
        return t;
    }

    public void Dispose() => _kmt?.Dispose();
}

/// <summary>Leitura avulsa do PC, para conferir numa máquina o que o Windows deixa ler (comando "rkzfps sensors").</summary>
public static class HealthProbe
{
    public static LoadSample Once(int pid)
    {
        using var s = new GameHealthSampler(pid, GpuSelector.HighPerformance(GpuAdapters.List())?.Luid);
        // Duas leituras: a primeira só serve de base para o tempo por thread.
        s.Sample(0, false, null, 0);
        Thread.Sleep(1000);
        return s.Sample(1, false, null, 0);
    }
}

/// <summary>
/// Temperatura e clock da placa de vídeo pela interface de kernel do
/// Windows (D3DKMT), a mesma que o Gerenciador de Tarefas usa. Funciona com
/// driver WDDM 2.4 ou mais novo; driver que não preenche devolve zero, que
/// vira "não disponível".
/// </summary>
internal sealed class GpuKmt : IDisposable
{
    private const int NodePerfData = 61;
    private const int AdapterPerfData = 62;
    private readonly uint _adapter;

    private GpuKmt(uint adapter) => _adapter = adapter;

    public static GpuKmt? Open(string luidKey)
    {
        if (luidKey.Length != 16
            || !int.TryParse(luidKey[..8], System.Globalization.NumberStyles.HexNumber, null, out var high)
            || !uint.TryParse(luidKey[8..], System.Globalization.NumberStyles.HexNumber, null, out var low))
            return null;
        try
        {
            var open = new OpenAdapterFromLuid { Luid = new Luid { Low = low, High = high } };
            return D3DKMTOpenAdapterFromLuid(ref open) == 0 ? new GpuKmt(open.Adapter) : null;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            // Windows antigo sem a função.
            return null;
        }
    }

    public (double? Temp, double? ClockMhz, double? ClockPct) Read()
    {
        double? temp = null, pct = null;
        if (Query<AdapterPerf>(AdapterPerfData, default) is { } a)
            temp = CounterMath.DeciCelsius(a.Temperature);
        // Nó 0 é o motor 3D na prática de todos os drivers de mercado. O
        // clock fica só em % do máximo: a unidade absoluta varia por driver (a
        // documentação diz Hz, e placa NVIDIA testada devolve dezenas de kHz),
        // e um MHz errado na tela seria número inventado.
        if (Query<NodePerf>(NodePerfData, new NodePerf { NodeOrdinal = 0 }) is { Frequency: > 0, MaxFrequency: > 0 } n)
            pct = Math.Min(100, 100.0 * n.Frequency / n.MaxFrequency);

        return (temp, null, pct);
    }

    private T? Query<T>(int type, T input) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(input, buffer, false);
            var q = new QueryAdapterInfo { Adapter = _adapter, Type = type, Data = buffer, DataSize = (uint)size };
            return D3DKMTQueryAdapterInfo(ref q) == 0 ? Marshal.PtrToStructure<T>(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        var close = new CloseAdapter { Adapter = _adapter };
        D3DKMTCloseAdapter(ref close);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint Low;
        public int High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAdapterFromLuid
    {
        public Luid Luid;
        public uint Adapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CloseAdapter
    {
        public uint Adapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryAdapterInfo
    {
        public uint Adapter;
        public int Type;
        public IntPtr Data;
        public uint DataSize;
    }

    // D3DKMT_ADAPTER_PERFDATA (d3dkmthk.h).
    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterPerf
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency;
        public ulong MaxMemoryFrequency;
        public ulong MaxMemoryFrequencyOc;
        public ulong MemoryBandwidth;
        public ulong PcieBandwidth;
        public uint FanRpm;
        public uint Power;
        public uint Temperature;
        public byte PowerStateOverride;
    }

    // D3DKMT_NODE_PERFDATA (d3dkmthk.h).
    [StructLayout(LayoutKind.Sequential)]
    private struct NodePerf
    {
        public uint NodeOrdinal;
        public uint PhysicalAdapterIndex;
        public ulong Frequency;
        public ulong MaxFrequency;
        public ulong MaxFrequencyOc;
        public uint Voltage;
        public uint VoltageMax;
        public uint VoltageMaxOc;
        public ulong MaxTransitionLatency;
    }

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapterFromLuid open);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapterInfo query);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTCloseAdapter(ref CloseAdapter close);
}
