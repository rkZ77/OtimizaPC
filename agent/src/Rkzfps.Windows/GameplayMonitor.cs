using System.Diagnostics;
using System.Runtime.InteropServices;
using Rkzfps.Core.Benchmark;
using Rkzfps.Core.Games;
using DisplayMode = Rkzfps.Core.Engine.DisplayMode;

namespace Rkzfps.Windows;

/// <summary>
/// Fica de olho nos jogos com perfil. Quando um abre, mede a partida inteira
/// com o PresentMon; quando fecha, calcula e entrega a sessão. Não altera
/// nada no PC e não lê nada do jogo além dos tempos de quadro.
/// </summary>
public sealed class GameplayMonitor(IReadOnlyList<GameProfile> profiles, string presentMonPath, string workDir, string appVersion) : IDisposable
{
    private const string SessionName = "RKZFPS_Gameplay";

    /// <summary>Partida mais longa que isso para de ser medida (arquivo temporário não cresce sem fim).</summary>
    private static readonly TimeSpan MaxCapture = TimeSpan.FromHours(4);

    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private string _status = "Parado.";

    public event Action<GameplaySession>? Recorded;
    public event Action<string>? StatusChanged;

    /// <summary>Uma vez por segundo durante a partida: jogo e FPS atual (null = acabou ou ainda sem quadros).</summary>
    public event Action<string, double?, double?>? LiveFps;

    /// <summary>Jogo aberto: id do perfil e caminho do executável (para o ícone na tela).</summary>
    public event Action<string, string>? GameSeen;

    /// <summary>Jogo abriu e a medição começou: id e nome do jogo (o modo Gaming Automático escuta aqui).</summary>
    public event Action<string, string>? GameStarted;

    /// <summary>
    /// A partida terminou, por qualquer motivo: jogo fechou, caiu, passou do
    /// limite de captura, erro na medição ou o app encerrando. Sempre vem
    /// depois de um GameStarted, para o modo Gaming nunca deixar nada ligado.
    /// </summary>
    public event Action<string>? GameEnded;

    /// <summary>A cada 5 s durante a partida: a leitura do PC (CPU, GPU, temperatura, memória). null = partida acabou.</summary>
    public event Action<LoadSample?>? LiveHealth;

    /// <summary>
    /// Reconhecer jogo sem perfil pela tela cheia. Ligado por padrão; quem
    /// só quer os jogos do catálogo desliga.
    /// </summary>
    public bool DetectUnknownGames { get; init; } = true;

    private int _candidatePid;
    private int _candidateHits;

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
            Status = "PresentMon não encontrado na pasta do RKZFPS. Reinstale o RKZFPS para medir o FPS das partidas.";
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
                {
                    GameStarted?.Invoke(found.Profile.Id, found.Profile.Name);
                    try
                    {
                        Capture(found.Profile, found.Process, found.Detected, ct);
                    }
                    finally
                    {
                        GameEnded?.Invoke(found.Profile.Id);
                    }
                }
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

    private (GameProfile Profile, Process Process, bool Detected)? FindGame()
    {
        if (FindProfiledGame() is { } known)
            return (known.Profile, known.Process, false);
        return DetectUnknownGames ? DetectForegroundGame() : null;
    }

    /// <summary>
    /// Jogo sem perfil: janela em tela cheia na frente, usando a GPU, que não é
    /// vídeo, navegador nem programa do Windows. Precisa aparecer em duas
    /// rodadas seguidas (10 s) para não medir quem só passou pela tela cheia.
    /// A consulta de GPU por processo só roda quando já há um candidato em
    /// tela cheia: no uso normal do PC ela nem acontece.
    /// </summary>
    private (GameProfile, Process, bool)? DetectForegroundGame()
    {
        var hwnd = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (hwnd == IntPtr.Zero || pid == Environment.ProcessId || !GameScreen.IsFullscreen(hwnd))
        {
            _candidateHits = 0;
            return null;
        }

        Process p;
        try
        {
            p = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var name = p.ProcessName;
        string? path = null;
        try
        {
            path = p.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Anti-cheat: sem caminho, decide pelo nome e pelo uso de GPU.
        }

        // Filtro barato antes da consulta de GPU.
        if (!Rkzfps.Core.Diagnostics.ProcessClassifier.IsLikelyGame(name, path, true, 100)
            || !Rkzfps.Core.Diagnostics.ProcessClassifier.IsLikelyGame(name, path, true, GameHealthSampler.Gpu3dOf(pid)))
        {
            _candidateHits = 0;
            p.Dispose();
            return null;
        }

        _candidateHits = _candidatePid == pid ? _candidateHits + 1 : 1;
        _candidatePid = pid;
        if (_candidateHits < 2)
        {
            p.Dispose();
            return null;
        }

        _candidateHits = 0;
        var profile = new GameProfile
        {
            Id = "exe-" + name.ToLowerInvariant(),
            Name = FriendlyName(path, name),
            Benchmark = new GameBenchmarkSpec { Process = name },
        };
        return (profile, p, true);
    }

    private static string FriendlyName(string? path, string fallback)
    {
        try
        {
            if (path is not null)
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                foreach (var n in new[] { info.ProductName, info.FileDescription })
                    if (!string.IsNullOrWhiteSpace(n) && n.Length <= 60)
                        return n.Trim();
            }
        }
        catch (FileNotFoundException)
        {
        }

        return fallback;
    }

    private (GameProfile Profile, Process Process)? FindProfiledGame()
    {
        // UMA lista de processos por rodada: GetProcessesByName para cada um
        // dos ~30 executáveis dos perfis pediria a lista inteira ao Windows 30
        // vezes a cada 5 segundos. O RKZFPS não pode pesar no PC que ele otimiza.
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

    private void Capture(GameProfile profile, Process game, bool detected, CancellationToken ct)
    {
        using var gameProcess = game;
        var startedAt = DateTimeOffset.Now;
        try
        {
            if (game.MainModule?.FileName is { } exe)
                GameSeen?.Invoke(profile.Id, exe);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Anti-cheat protege o processo de alguns jogos: sem ícone, a tela usa as iniciais.
        }
        var csv = Path.Combine(workDir, $"{startedAt:yyyyMMdd-HHmmss}-{profile.Id}.csv");
        Status = $"Medindo {profile.Name}. Jogue normalmente: o resultado aparece quando o jogo fechar.";

        var pmLog = new System.Text.StringBuilder();
        var live = new LiveFpsMeter(game.Id);
        // O PresentMon entrega o CSV pela saída padrão e o RKZFPS grava o
        // arquivo. Ler o arquivo que o PresentMon grava não dá: ele o trava
        // enquanto escreve. Assim o mesmo fluxo alimenta o FPS ao vivo.
        var sink = new CsvSink(csv, live);
        var self = Process.GetCurrentProcess();
        var previousPriority = self.PriorityClass;
        var foreground = new List<bool>();
        var load = new List<LoadSample>();
        var topApp = new TopAppSampler(game.Id);
        DateTimeOffset endedAt;
        double? cpu, gpu;
        DisplayMode? screen = null;
        // Placas do PC lidas uma vez por partida (troca de placa com o jogo aberto não acontece).
        var adapters = GpuAdapters.List();
        (IReadOnlyDictionary<string, double> ByLuid, int Samples) gpuLoad = (new Dictionary<string, double>(), 0);
        using (var pm = StartPresentMon(game.Id, sink, pmLog))
        {
            using var health = new GameHealthSampler(game.Id, GpuSelector.HighPerformance(adapters)?.Luid);
            using var stopSampling = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task? sampling = null;
            try
            {
                // Durante a partida o RKZFPS e o PresentMon ficam abaixo do normal:
                // se o processador apertar, o jogo vem primeiro. E a leitura do PC
                // (WMI e driver, a parte mais cara daqui) é feita a cada 5 s.
                TrySetPriority(self, ProcessPriorityClass.BelowNormal);
                TrySetPriority(pm, ProcessPriorityClass.BelowNormal);
                var watch = Stopwatch.StartNew();

                // Leitura do PC numa tarefa própria: uma consulta lenta ao WMI não
                // atrasa o registro de primeiro plano, que é por segundo.
                sampling = Task.Run(() =>
                {
                    while (!stopSampling.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)))
                    {
                        var t = (int)watch.Elapsed.TotalSeconds;
                        var hwnd = Native.GetForegroundWindow();
                        var fg = IsForeground(game.Id);
                        // Resolução lida depois do carregamento, no monitor em que o
                        // jogo está: jogo em tela cheia exclusiva muda o modo da tela.
                        if (screen is null && fg && t >= GameplayAnalyzer.WarmupSeconds)
                            screen = GameScreen.Mode(hwnd);
                        var (app, appCpu) = topApp.Sample();
                        var sample = health.Sample(t, fg, app, Math.Round(appCpu, 1));
                        lock (load)
                            load.Add(sample);
                        LiveHealth?.Invoke(sample);
                    }
                });

                while (!ct.IsCancellationRequested && !HasExited(game) && watch.Elapsed < MaxCapture)
                {
                    // Um registro por segundo de relógio, não por volta do laço:
                    // os quadros do PresentMon têm o tempo real, e o filtro de
                    // primeiro plano precisa bater com ele.
                    var fg = IsForeground(game.Id);
                    var second = (int)watch.Elapsed.TotalSeconds;
                    lock (foreground)
                        while (foreground.Count <= second)
                            foreground.Add(fg);

                    var (fps, low) = sink.Current;
                    LiveFps?.Invoke(profile.Name, fps, low);
                    ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                }

                endedAt = DateTimeOffset.Now;
            }
            finally
            {
                stopSampling.Cancel();
                try
                {
                    sampling?.Wait(TimeSpan.FromSeconds(15));
                }
                catch (AggregateException)
                {
                }

                (cpu, gpu) = health.Averages();
                gpuLoad = health.GameGpuLoad();
                // Mesmo com erro no meio, o PresentMon nunca fica rodando sozinho.
                StopPresentMon(pm);
                sink.Close();
                TrySetPriority(self, previousPriority);
                LiveFps?.Invoke(profile.Name, null, null);
                LiveHealth?.Invoke(null);
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
                    ? $"{profile.Name}: o Windows não deixou medir sem permissão. Abra o RKZFPS como administrador e jogue de novo."
                    : $"{profile.Name}: nenhum quadro registrado. O jogo fechou antes de desenhar na tela.";
                return;
            }

            List<double> frametimes;
            List<FpsPoint> timeline;
            using (var reader = new StreamReader(csv))
                (frametimes, timeline) = GameplayAnalyzer.Analyze(PresentMonCsv.ReadTimedFrames(reader, game.Id), foreground);

            var (built, reason) = GameplayAnalyzer.Build(frametimes, profile.Id, profile.Name, startedAt, endedAt, cpu, gpu, screen?.RefreshHz ?? PrimaryHz(), appVersion, timeline, load);
            if (built is null)
            {
                Status = $"{profile.Name}: {reason}";
                return;
            }

            var session = built with
            {
                ScreenWidth = screen?.Width,
                ScreenHeight = screen?.Height,
                GameSettings = ReadGameSettings(profile),
                Detected = detected,
                Gpu = GpuSelector.Evaluate(adapters, gpuLoad.ByLuid, gpuLoad.Samples),
            };
            Recorded?.Invoke(session);
            Status = $"{profile.Name}: partida registrada, {session.Stats.AvgFps:0} FPS médio e 1% low {session.Stats.Low1Fps:0}.";
        }
        finally
        {
            // O CSV bruto passa de 100 MB por hora: só os números ficam.
            // RKZFPS_KEEP_GAMEPLAY_CSV=1 guarda o arquivo para diagnóstico.
            if (Environment.GetEnvironmentVariable("RKZFPS_KEEP_GAMEPLAY_CSV") != "1")
                TryDelete(csv);
        }
    }

    /// <summary>
    /// Qual programa, fora o jogo e o RKZFPS, mais usou processador desde a última
    /// amostra. Só programas do usuário: serviço do Windows e antivírus não são
    /// "culpados" que a pessoa possa fechar. O nome fica só no PC.
    /// </summary>
    private sealed class TopAppSampler(int gamePid)
    {
        private Dictionary<int, (string Name, TimeSpan Cpu)> _last = [];
        private DateTime _lastAt = DateTime.UtcNow;

        public (string? App, double CpuPercent) Sample()
        {
            var now = DateTime.UtcNow;
            var current = new Dictionary<int, (string, TimeSpan)>();
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    if (p.Id == gamePid || p.Id == Environment.ProcessId)
                        continue;
                    try
                    {
                        current[p.Id] = (p.ProcessName, p.TotalProcessorTime);
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
                    {
                    }
                }
            }

            var seconds = (now - _lastAt).TotalSeconds * Environment.ProcessorCount;
            var top = current
                .Where(kv => _last.ContainsKey(kv.Key) && string.Equals(_last[kv.Key].Name, kv.Value.Item1, StringComparison.Ordinal))
                .Where(kv => !kv.Value.Item1.StartsWith("PresentMon", StringComparison.OrdinalIgnoreCase))
                .Where(kv => Rkzfps.Core.Diagnostics.ProcessClassifier.Classify(kv.Value.Item1) == Rkzfps.Core.Diagnostics.ProcessCategory.User)
                // Programa, não processo: as abas do navegador somam.
                .GroupBy(kv => kv.Value.Item1, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Name: g.Key, Cpu: g.Sum(kv => (kv.Value.Item2 - _last[kv.Key].Cpu).TotalSeconds)))
                .OrderByDescending(x => x.Cpu)
                .FirstOrDefault();
            _last = current;
            _lastAt = now;
            if (top.Name is null || seconds <= 0)
                return (null, 0);
            var name = top.Name.Length > 0 ? char.ToUpperInvariant(top.Name[0]) + top.Name[1..] : top.Name;
            return (name, Math.Max(0, 100 * top.Cpu / seconds));
        }
    }

    /// <summary>Recebe as linhas do PresentMon: grava o CSV da partida e alimenta o FPS ao vivo.</summary>
    private sealed class CsvSink(string path, LiveFpsMeter live)
    {
        private readonly object _lock = new();
        private StreamWriter? _writer;
        private bool _header;

        public bool HasFrames { get; private set; }

        /// <summary>FPS médio e do pior quadro da janela recente.</summary>
        public (double? Fps, double? Low) Current
        {
            get
            {
                lock (_lock)
                    return (live.Current, live.Low);
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

    /// <summary>
    /// Opções gráficas do jogo, lidas depois que ele fechou (o arquivo está
    /// livre e tem o que foi usado). Só as chaves do perfil. Falha de leitura
    /// devolve vazio: a tela diz "configuração não lida".
    /// </summary>
    private static IReadOnlyDictionary<string, string> ReadGameSettings(GameProfile profile)
    {
        var keys = profile.GraphicsKeys();
        if (profile.Config is not { } source || keys.Count == 0 || GameLocator.ConfigPath(source) is not { } path)
            return new Dictionary<string, string>();
        try
        {
            var all = ConfigFiles.Parse(source.Format, TextFiles.Read(path).Text);
            return keys.Where(all.ContainsKey).ToDictionary(k => k, k => all[k], StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Dictionary<string, string>();
        }
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

/// <summary>Tela em que a janela do jogo está: se ela ocupa o monitor inteiro e em que modo o monitor está.</summary>
internal static class GameScreen
{
    public static bool IsFullscreen(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || Native.IsIconic(hwnd) || !Native.GetWindowRect(hwnd, out var w) || Monitor(hwnd) is not { } m)
            return false;
        // Tela cheia exclusiva ou janela sem borda: cobre o monitor todo.
        return w.Left <= m.Rect.Left && w.Top <= m.Rect.Top && w.Right >= m.Rect.Right && w.Bottom >= m.Rect.Bottom;
    }

    public static DisplayMode? Mode(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && Monitor(hwnd) is { } m ? DisplayApi.Current(m.Device) : null;

    private static (Native.Rect Rect, string Device)? Monitor(IntPtr hwnd)
    {
        var mon = Native.MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        if (mon == IntPtr.Zero)
            return null;
        var info = new Native.MonitorInfoEx { Size = Marshal.SizeOf<Native.MonitorInfoEx>() };
        return Native.GetMonitorInfo(mon, ref info) ? (info.Monitor, info.Device) : null;
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MonitorInfoEx
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public uint Flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string Device;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
    }
}
