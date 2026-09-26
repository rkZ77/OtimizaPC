using System.Diagnostics;
using System.Runtime.InteropServices;
using Fpsx.Core.Benchmark;
using Fpsx.Core.Games;

namespace Fpsx.Windows;

/// <summary>
/// Fica de olho nos jogos com perfil. Quando um abre, mede a partida inteira
/// com o PresentMon; quando fecha, calcula e entrega a sessão. Não altera
/// nada no PC e não lê nada do jogo além dos tempos de quadro.
/// </summary>
public sealed class GameplayMonitor(IReadOnlyList<GameProfile> profiles, string presentMonPath, string workDir, string appVersion) : IDisposable
{
    private const string SessionName = "FPSX_Gameplay";

    /// <summary>Partida mais longa que isso para de ser medida (arquivo temporário não cresce sem fim).</summary>
    private static readonly TimeSpan MaxCapture = TimeSpan.FromHours(4);

    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private string _status = "Parado.";

    public event Action<GameplaySession>? Recorded;
    public event Action<string>? StatusChanged;

    /// <summary>Uma vez por segundo durante a partida: jogo e FPS atual (null = acabou ou ainda sem quadros).</summary>
    public event Action<string, double?>? LiveFps;

    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            StatusChanged?.Invoke(value);
        }
    }

    public bool Running => _loop is { IsCompleted: false };

    public void Start()
    {
        if (Running)
            return;
        if (!File.Exists(presentMonPath))
        {
            Status = "PresentMon não encontrado na pasta do FPSX. Reinstale o FPSX para medir o FPS das partidas.";
            return;
        }

        Directory.CreateDirectory(workDir);
        Status = "Aguardando um jogo abrir.";
        _loop = Task.Run(() => Loop(_cts.Token));
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(20));
        }
        catch (AggregateException)
        {
        }

        _cts.Dispose();
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (FindGame() is { } found)
                    Capture(found.Profile, found.Process, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Uma partida que falhou não pode desligar o monitor.
                Status = $"Não foi possível medir a última partida: {ex.Message}";
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private (GameProfile Profile, Process Process)? FindGame()
    {
        // UMA lista de processos por rodada: GetProcessesByName para cada um
        // dos ~30 executáveis dos perfis pediria a lista inteira ao Windows 30
        // vezes a cada 5 segundos. O FPSX não pode pesar no PC que ele otimiza.
        var running = Process.GetProcesses();
        try
        {
            var byName = running.ToLookup(p => p.ProcessName, StringComparer.OrdinalIgnoreCase);
            foreach (var profile in profiles)
            {
                // Todos os executáveis do jogo: o FC muda de nome a cada ano
                // (FC26.exe, FC27.exe) e alguns jogos têm versão DX12 separada.
                foreach (var name in profile.Benchmark.MeasuredProcesses(profile.Detect))
                {
                    foreach (var p in byName[name])
                    {
                        if (profile.Benchmark.WindowTitle is { } title && !SafeTitle(p).Contains(title, StringComparison.OrdinalIgnoreCase))
                            continue;
                        // Devolve um objeto novo e libera a lista inteira abaixo.
                        try
                        {
                            return (profile, Process.GetProcessById(p.Id));
                        }
                        catch (ArgumentException)
                        {
                            // Fechou entre a lista e agora.
                        }
                    }
                }
            }

            return null;
        }
        finally
        {
            foreach (var p in running)
                p.Dispose();
        }
    }

    private static string SafeTitle(Process p)
    {
        try
        {
            return p.MainWindowTitle ?? "";
        }
        catch (InvalidOperationException)
        {
            return "";
        }
    }

    private void Capture(GameProfile profile, Process game, CancellationToken ct)
    {
        using var gameProcess = game;
        var startedAt = DateTimeOffset.Now;
        var csv = Path.Combine(workDir, $"{startedAt:yyyyMMdd-HHmmss}-{profile.Id}.csv");
        Status = $"Medindo {profile.Name}. Jogue normalmente: o resultado aparece quando o jogo fechar.";

        var pmLog = new System.Text.StringBuilder();
        var live = new LiveFpsMeter(game.Id);
        // O PresentMon entrega o CSV pela saída padrão e o FPSX grava o
        // arquivo. Ler o arquivo que o PresentMon grava não dá: ele o trava
        // enquanto escreve. Assim o mesmo fluxo alimenta o FPS ao vivo.
        var sink = new CsvSink(csv, live);
        var self = Process.GetCurrentProcess();
        var previousPriority = self.PriorityClass;
        var foreground = new List<bool>();
        DateTimeOffset endedAt;
        double? cpu, gpu;
        using (var pm = StartPresentMon(game.Id, sink, pmLog))
        {
            try
            {
                // Durante a partida o FPSX e o PresentMon ficam abaixo do normal:
                // se o processador apertar, o jogo vem primeiro. E o uso de CPU e
                // GPU (WMI, a consulta mais cara daqui) é lido a cada 5 s.
                TrySetPriority(self, ProcessPriorityClass.BelowNormal);
                TrySetPriority(pm, ProcessPriorityClass.BelowNormal);
                using var sampler = new SystemSampler(TimeSpan.FromSeconds(5));
                var watch = Stopwatch.StartNew();
                while (!ct.IsCancellationRequested && !HasExited(game) && watch.Elapsed < MaxCapture)
                {
                    foreground.Add(IsForeground(game.Id));
                    LiveFps?.Invoke(profile.Name, sink.CurrentFps);
                    ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                }

                endedAt = DateTimeOffset.Now;
                (cpu, gpu, _) = sampler.Stop();
            }
            finally
            {
                // Mesmo com erro no meio, o PresentMon nunca fica rodando sozinho.
                StopPresentMon(pm);
                sink.Close();
                TrySetPriority(self, previousPriority);
                LiveFps?.Invoke(profile.Name, null);
            }
        }

        try
        {
            if (!sink.HasFrames)
            {
                // Sem CSV pode ser permissão (o PresentMon diz) ou o jogo não
                // ter desenhado nada. Mensagem de permissão sem ser permissão
                // mandaria a pessoa atrás do problema errado.
                string log;
                lock (pmLog)
                    log = pmLog.ToString();
                Status = log.Contains("privilege", StringComparison.OrdinalIgnoreCase) && log.Contains("error", StringComparison.OrdinalIgnoreCase)
                         || log.Contains("access denied", StringComparison.OrdinalIgnoreCase)
                    ? $"{profile.Name}: o Windows não deixou medir sem permissão. Abra o FPSX como administrador e jogue de novo."
                    : $"{profile.Name}: nenhum quadro registrado. O jogo fechou antes de desenhar na tela.";
                return;
            }

            List<double> frametimes;
            using (var reader = new StreamReader(csv))
                frametimes = GameplayAnalyzer.Filter(PresentMonCsv.ReadTimedFrames(reader, game.Id), foreground);

            var (session, reason) = GameplayAnalyzer.Build(frametimes, profile.Id, profile.Name, startedAt, endedAt, cpu, gpu, PrimaryHz(), appVersion);
            if (session is null)
            {
                Status = $"{profile.Name}: {reason}";
                return;
            }

            Recorded?.Invoke(session);
            Status = $"{profile.Name}: partida registrada, {session.Stats.AvgFps:0} FPS médio e 1% low {session.Stats.Low1Fps:0}.";
        }
        finally
        {
            // O CSV bruto passa de 100 MB por hora: só os números ficam.
            // FPSX_KEEP_GAMEPLAY_CSV=1 guarda o arquivo para diagnóstico.
            if (Environment.GetEnvironmentVariable("FPSX_KEEP_GAMEPLAY_CSV") != "1")
                TryDelete(csv);
        }
    }

    /// <summary>Recebe as linhas do PresentMon: grava o CSV da partida e alimenta o FPS ao vivo.</summary>
    private sealed class CsvSink(string path, LiveFpsMeter live)
    {
        private readonly object _lock = new();
        private StreamWriter? _writer;
        private bool _header;

        public bool HasFrames { get; private set; }

        public double? CurrentFps
        {
            get
            {
                lock (_lock)
                    return live.Current;
            }
        }

        /// <summary>true = era linha do CSV; false = texto de aviso do PresentMon.</summary>
        public bool Offer(string line)
        {
            var isHeader = line.StartsWith("Application,", StringComparison.Ordinal);
            if (!isHeader && (!_header || line.Count(c => c == ',') < 8))
                return false;
            lock (_lock)
            {
                _writer ??= new StreamWriter(path, append: false) { AutoFlush = false };
                _writer.WriteLine(line);
                live.Add(line);
                if (isHeader)
                    _header = true;
                else
                    HasFrames = true;
            }

            return true;
        }

        public void Close()
        {
            lock (_lock)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }
    }

    private Process StartPresentMon(int pid, CsvSink sink, System.Text.StringBuilder log)
    {
        var psi = new ProcessStartInfo(presentMonPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // Por PID e não por nome: sem administrador o PresentMon pode não
        // resolver o nome do processo, mas o PID vem em todo evento.
        foreach (var arg in new[]
                 {
                     "--process_id", pid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     // Sem rastrear GPU, tela e entrada: o FPS sai do intervalo
                     // entre apresentações, e cada rastreamento extra é custo à toa.
                     "--output_stdout", "--v1_metrics", "--no_console_stats", "--no_track_input", "--no_track_gpu", "--no_track_display",
                     "--session_name", SessionName, "--stop_existing_session", "--terminate_on_proc_exit",
                 })
            psi.ArgumentList.Add(arg);
        var p = Process.Start(psi) ?? throw new InvalidOperationException("Falha ao iniciar o PresentMon.");
        // Sem ler a saída o buffer enche e o PresentMon trava. Guarda o texto
        // para explicar uma falha, com teto para não crescer numa partida longa.
        void Keep(object _, DataReceivedEventArgs e)
        {
            if (e.Data is null)
                return;
            lock (log)
                if (log.Length < 8_000)
                    log.AppendLine(e.Data);
        }

        p.OutputDataReceived += (s, e) =>
        {
            if (e.Data is not null && !sink.Offer(e.Data))
                Keep(s, e);
        };
        p.ErrorDataReceived += Keep;
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    private void StopPresentMon(Process pm)
    {
        if (pm.WaitForExit(15_000))
            return;
        // Não fechou sozinho (app encerrando no meio da partida): encerra e
        // fecha a sessão de rastreamento, que senão ficaria aberta no Windows.
        try
        {
            pm.Kill(entireProcessTree: true);
            pm.WaitForExit(5_000);
            var cleanup = new ProcessStartInfo(presentMonPath) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "--session_name", SessionName, "--stop_existing_session", "--timed", "1", "--terminate_after_timed", "--no_console_stats", "--no_csv" })
                cleanup.ArgumentList.Add(arg);
            Process.Start(cleanup)?.WaitForExit(10_000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static void TrySetPriority(Process p, ProcessPriorityClass priority)
    {
        try
        {
            p.PriorityClass = priority;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static bool HasExited(Process p)
    {
        try
        {
            return p.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static bool IsForeground(int pid)
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || Native.IsIconic(hwnd))
            return false;
        Native.GetWindowThreadProcessId(hwnd, out var owner);
        return owner == pid;
    }

    private static int? PrimaryHz()
    {
        var primary = DisplayApi.Devices().FirstOrDefault(d => d.Primary);
        return primary.Device is null ? null : DisplayApi.Current(primary.Device)?.RefreshHz;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static class Native
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);
    }
}
