using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using Fpsx.Core.Benchmark;
using Fpsx.Core.Engine;
using Fpsx.Core.Json;
using Fpsx.Windows;
using Microsoft.Win32;

namespace Fpsx.App.ViewModels;

public sealed class RunItem(BenchmarkRun run) : ObservableObject
{
    private bool _before;
    private bool _after;

    public BenchmarkRun Run { get; } = run;
    public string Title { get; } = $"{run.At.ToLocalTime():dd/MM HH:mm} · {run.Label}";
    public string Stats { get; } = string.Format(CultureInfo.InvariantCulture,
        "FPS {0:0.0} · 1% low {1:0.0} · 0.1% low {2:0.0} · frametime {3:0.00} ms · {4} stutters",
        run.Stats.AvgFps, run.Stats.Low1Fps, run.Stats.Low01Fps, run.Stats.AvgFrametimeMs, run.Stats.StutterCount);

    public bool IsBefore
    {
        get => _before;
        set { if (Set(ref _before, value) && value) IsAfter = false; }
    }

    public bool IsAfter
    {
        get => _after;
        set { if (Set(ref _after, value) && value) IsBefore = false; }
    }
}

/// <summary>
/// FPSX Benchmark: captura com PresentMon e compara antes/depois com limiar
/// de ruído. O número que aparece é o medido; sem diferença, a tela diz isso.
/// </summary>
public sealed class BenchmarkViewModel : PageViewModel
{
    private readonly AppHost _host = AppHost.Current;
    private string _presentMon;
    private string _label = "antes";
    private int _duration = 60;
    private string _result = "";

    public BenchmarkViewModel()
    {
        _presentMon = _host.Ctx.Settings.PresentMonPath ?? Path.Combine(AppContext.BaseDirectory, "tools", "PresentMon.exe");
        BrowseCommand = new RelayCommand(Browse);
        RunCommand = new AsyncCommand(() => Busy(Capture), () => !IsBusy && Allowed);
        CompareCommand = new RelayCommand(Compare, () => Runs.Any(r => r.IsBefore) && Runs.Any(r => r.IsAfter));
        GetPresentMonCommand = new RelayCommand(() => AppHost.OpenUrl("https://github.com/GameTechDev/PresentMon/releases"));
        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppHost.License))
                Raise(nameof(Allowed));
        };
        LoadRuns();
    }

    public override string Title => "Benchmark";

    public ICommand BrowseCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand CompareCommand { get; }
    public ICommand GetPresentMonCommand { get; }

    public bool Allowed => _host.Allows(Feature.Benchmark);
    public string PlanNote => $"O FPSX Benchmark faz parte do plano {PlanFeatures.RequiredPlan(Feature.Benchmark)}.";
    public string Method => _host.Ctx.GameProfiles.FirstOrDefault(g => g.Id == "cs2")?.Benchmark.Method ?? "";
    public ObservableCollection<RunItem> Runs { get; } = [];
    public string[] Labels { get; } = ["antes", "depois"];

    public string PresentMonPath
    {
        get => _presentMon;
        set => Set(ref _presentMon, value);
    }

    public string Label
    {
        get => _label;
        set => Set(ref _label, value);
    }

    public int Duration
    {
        get => _duration;
        set => Set(ref _duration, Math.Clamp(value, 10, 600));
    }

    public string Result
    {
        get => _result;
        private set => Set(ref _result, value);
    }

    private void Browse()
    {
        var dialog = new OpenFileDialog { Filter = "PresentMon|PresentMon*.exe", Title = "Selecione o PresentMon.exe" };
        if (dialog.ShowDialog() == true)
        {
            PresentMonPath = dialog.FileName;
            _host.Ctx.Storage.SaveSettings(_host.Ctx.Settings with { PresentMonPath = dialog.FileName });
        }
    }

    private async Task Capture()
    {
        var profile = _host.Ctx.GameProfiles.First(g => g.Id == "cs2");
        Directory.CreateDirectory(_host.Ctx.BenchmarksDir);
        var csv = Path.Combine(_host.Ctx.BenchmarksDir, $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Label}.csv");
        Progress = $"Capturando {Duration} s do {profile.Name}. Jogue o cenário de teste agora...";
        var (run, _) = await Task.Run(() => BenchmarkRunner.Capture(new BenchmarkRequest(PresentMonPath, profile.Benchmark.Process, Duration, csv), Label, profile.Id));
        File.WriteAllText(Path.Combine(_host.Ctx.BenchmarksDir, run.Id + ".json"), JsonSerializer.Serialize(run, FpsxJson.Options));
        LoadRuns();
        Result = run.Stats.Sufficient
            ? $"Captura concluída: FPS médio {run.Stats.AvgFps:0.0}, 1% low {run.Stats.Low1Fps:0.0}."
            : "Captura muito curta para um resultado confiável (mínimo 10 s e 300 quadros).";
    }

    private void Compare()
    {
        var before = Runs.Where(r => r.IsBefore).Select(r => r.Run.Stats).ToList();
        var after = Runs.Where(r => r.IsAfter).Select(r => r.Run.Stats).ToList();
        var cmp = BenchmarkComparer.Compare(before, after);
        var lines = cmp.Metrics.Select(m => string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.0} → {2:0.0} ({3}{4:0.0}%){5}",
            m.Metric, m.Before, m.After, m.DeltaPercent >= 0 ? "+" : "", m.DeltaPercent, m.Significant ? "" : " · dentro da variação normal"));
        Result = string.Join("\n", lines) + "\n\n" + cmp.Verdict + "\n\n" + string.Join("\n", cmp.Warnings) + "\n" + cmp.Method;
    }

    private void LoadRuns()
    {
        Runs.Clear();
        if (!Directory.Exists(_host.Ctx.BenchmarksDir))
            return;
        foreach (var file in Directory.EnumerateFiles(_host.Ctx.BenchmarksDir, "*.json").OrderByDescending(f => f))
        {
            try
            {
                if (JsonSerializer.Deserialize<BenchmarkRun>(File.ReadAllText(file), FpsxJson.Options) is { } run)
                    Runs.Add(new RunItem(run) { IsBefore = run.Label == "antes", IsAfter = run.Label == "depois" });
            }
            catch (JsonException)
            {
            }
        }
    }
}
