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
        foreach (var profile in profiles)
        {
            var exe = profile.Benchmark.Process;
            if (string.IsNullOrEmpty(exe))
                continue;
            var name = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe[..^4] : exe;
            foreach (var p in Process.GetProcessesByName(name))
            {
                if (profile.Benchmark.WindowTitle is { } title && !SafeTitle(p).Contains(title, StringComparison.OrdinalIgnoreCase))
                {
                    p.Dispose();
                    continue;
                }

                return (profile, p);
            }
        }

        return null;
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
        using var _ = game;
        var startedAt = DateTimeOffset.Now;
        var csv = Path.Combine(workDir, $"{startedAt:yyyyMMdd-HHmmss}-{profile.Id}.csv");
        Status = $"Medindo {profile.Name}. Jogue normalmente: o resultado aparece quando o jogo fechar.";

        var pmLog = new System.Text.StringBuilder();
        using var pm = StartPresentMon(game.Id, csv, pmLog);
        using var sampler = new SystemSampler();
        var foreground = new List<bool>();
        var watch = Stopwatch.StartNew();
        while (!ct.IsCancellationRequested && !HasExited(game) && watch.Elapsed < MaxCapture)
        {
            foreground.Add(IsForeground(game.Id));
            ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
        }

        var endedAt = DateTimeOffset.Now;
        var (cpu, gpu, _) = sampler.Stop();
        StopPresentMon(pm);

        try
        {
            if (!File.Exists(csv))
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

    private Process StartPresentMon(int pid, string csv, System.Text.StringBuilder log)
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
                     "--output_file", csv, "--v1_metrics", "--no_console_stats", "--no_track_input",
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

        p.OutputDataReceived += Keep;
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
