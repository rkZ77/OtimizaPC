using Fpsx.Core.Diagnostics;
using Fpsx.Core.Engine;
using Fpsx.Core.Model;

namespace Fpsx.Core.Reporting;

public sealed record ReadinessRow(string Area, HealthStatus Status, string Summary);

public sealed record Report
{
    public DateTimeOffset GeneratedAt { get; init; }
    public string CatalogVersion { get; init; } = "";
    public string ProfileId { get; init; } = "";
    public IReadOnlyDictionary<string, string> Hardware { get; init; } = new Dictionary<string, string>();

    /// <summary>Performance Readiness (seção 26): status por área, sem nota numérica.</summary>
    public IReadOnlyList<ReadinessRow> Readiness { get; init; } = [];

    public int ProblemsFound { get; init; }
    public int Recommended { get; init; }
    public int AlreadyOptimal { get; init; }
    public int NoActionNeeded { get; init; }
    public IReadOnlyList<Finding> Findings { get; init; } = [];
    public IReadOnlyList<OptimizationSummary> Optimizations { get; init; } = [];
}

public sealed record OptimizationSummary(
    string Id,
    string Name,
    Classification Classification,
    RiskLevel Risk,
    string ImpactArea,
    Decision Decision,
    Potential Potential,
    string Reason,
    string? Warning,
    bool AutoSelected,
    IReadOnlyList<ProposalSummary> Proposals);

public sealed record ProposalSummary(string Id, string Title, IReadOnlyList<string> Changes, bool Reversible, bool RequiresReboot);

public static class ReportBuilder
{
    // Qual otimização alimenta qual linha do Readiness.
    private static readonly Dictionary<string, string> OptimizationArea = new()
    {
        ["game-mode-enable"] = Areas.GameMode,
        ["game-capture-background-recording-disable"] = Areas.Capture,
        ["power-plan-leave-power-saver"] = Areas.Power,
        ["power-plan-high-performance"] = Areas.Power,
        ["display-refresh-rate-max"] = Areas.Display,
        ["startup-entry-disable"] = Areas.Startup,
    };

    private static readonly string[] AreaOrder =
    [
        Areas.Windows, Areas.Cpu, Areas.Gpu, Areas.Driver, Areas.Ram, Areas.Storage, Areas.Startup,
        Areas.GameMode, Areas.Capture, Areas.Power, Areas.Display, Areas.Network, Areas.Game, Areas.Security,
    ];

    public static Report Build(ScanResult scan)
    {
        var s = scan.Snapshot;
        var rows = new List<ReadinessRow>();

        foreach (var area in AreaOrder)
        {
            var findings = scan.Findings.Where(f => f.Area == area).ToList();
            var opts = scan.Optimizations.Where(o => OptimizationArea.TryGetValue(o.Definition.Id, out var a) && a == area).ToList();
            if (findings.Count == 0 && opts.Count == 0)
                continue;

            var statuses = findings.Select(f => f.Status).Concat(opts.Select(o => Map(o.Decision))).ToList();
            var worst = Worst(statuses);
            var summary = area == Areas.Startup
                ? StartupSummary(scan)
                : findings.Where(f => f.Status == worst).Select(f => f.Title).FirstOrDefault()
                  ?? opts.Where(o => Map(o.Decision) == worst).Select(o => o.Reason).FirstOrDefault()
                  ?? "";
            rows.Add(new ReadinessRow(area, worst, summary));
        }

        var optimizations = scan.Optimizations.Select(o => new OptimizationSummary(
            o.Definition.Id, o.Definition.Name, o.Definition.Classification, o.Definition.Risk, o.Definition.ImpactArea,
            o.Decision, o.Evaluation.Potential, o.Reason, o.Evaluation.Warning, o.AutoSelected,
            o.Evaluation.Proposals.Select(p => new ProposalSummary(p.Id, p.Title, p.Changes.Select(c => c.Describe()).ToList(),
                p.Changes.All(c => c.Reversible), p.Changes.Any(c => c.RequiresReboot))).ToList())).ToList();

        return new Report
        {
            GeneratedAt = s.CapturedAt,
            CatalogVersion = scan.CatalogVersion,
            ProfileId = scan.ProfileId,
            Hardware = Hardware(s),
            Readiness = rows,
            ProblemsFound = scan.Findings.Count(f => f.Status is HealthStatus.Problem or HealthStatus.Attention),
            Recommended = scan.Optimizations.Count(o => o.Decision == Decision.Recommended),
            AlreadyOptimal = scan.Optimizations.Count(o => o.Decision == Decision.AlreadyOptimal) + scan.Findings.Count(f => f.Status == HealthStatus.Ok),
            NoActionNeeded = scan.Optimizations.Count(o => o.Decision == Decision.NotApplicable),
            Findings = scan.Findings,
            Optimizations = optimizations,
        };
    }

    private static string StartupSummary(ScanResult scan)
    {
        var enabled = scan.Snapshot.Startup.Count(e => e.Enabled);
        var proposals = scan.Optimizations.FirstOrDefault(o => o.Definition.Id == "startup-entry-disable")?.Evaluation.Proposals.Count ?? 0;
        return $"{enabled} programa(s) iniciam com o Windows, {proposals} podem ser desligados por escolha sua";
    }

    private static HealthStatus Map(Decision d) => d switch
    {
        Decision.Recommended => HealthStatus.Attention,
        Decision.AlreadyOptimal or Decision.NotApplicable => HealthStatus.Ok,
        Decision.Unknown => HealthStatus.Unknown,
        _ => HealthStatus.Info,
    };

    // Unknown não esconde um Problem: ordem de gravidade explícita.
    private static HealthStatus Worst(IEnumerable<HealthStatus> statuses)
    {
        HealthStatus[] order = [HealthStatus.Problem, HealthStatus.Attention, HealthStatus.Unknown, HealthStatus.Info, HealthStatus.Ok];
        var set = statuses.ToHashSet();
        return order.FirstOrDefault(set.Contains, HealthStatus.Ok);
    }

    private static Dictionary<string, string> Hardware(SystemSnapshot s)
    {
        var gpu = s.Gpus.FirstOrDefault(g => !g.LikelyIntegrated) ?? s.Gpus.FirstOrDefault();
        var system = s.Disks.FirstOrDefault(d => d.IsSystemDrive);
        return new Dictionary<string, string>
        {
            ["Windows"] = s.Os.Build > 0 ? $"{s.Os.Caption} (build {s.Os.Build})" : "desconhecido",
            ["CPU"] = s.Cpu?.Name ?? "desconhecida",
            ["GPU"] = gpu?.Name ?? "desconhecida",
            ["RAM"] = s.Memory is { } m ? Fmt.Gb(m.TotalBytes) : "desconhecida",
            ["Armazenamento"] = system is null ? "desconhecido" : $"{system.DriveLetter} {(system.Media == MediaKind.Ssd ? "SSD" : system.Media == MediaKind.Hdd ? "HDD" : "")} {system.BusType}".Trim(),
            ["Tipo"] = s.Power is null ? "desconhecido" : s.Power.HasBattery ? "Notebook" : "Desktop",
            ["Placa-mãe"] = s.BoardProduct.Length > 0 ? $"{ShortBrand(s.BoardManufacturer)} {s.BoardProduct}".Trim() : "desconhecida",
            ["BIOS"] = s.BiosVersion.Length > 0 ? s.BiosVersion + (s.BiosDate is { } d ? $" ({d:MM/yyyy})" : "") : "desconhecida",
        };
    }

    // "Gigabyte Technology Co., Ltd." vira "Gigabyte": o resto é ruído na tela.
    private static string ShortBrand(string m) =>
        m.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() switch
        {
            null => "",
            "Micro-Star" => "MSI",
            "ASUSTeK" => "ASUS",
            var first => first,
        };
}
