using System.Globalization;
using System.Text.Json;
using Fpsx.Client;
using Fpsx.Core.Benchmark;
using Fpsx.Core.Engine;
using Fpsx.Core.Games;
using Fpsx.Core.Json;
using Fpsx.Core.Model;
using Fpsx.Core.Reporting;
using Fpsx.Windows;

namespace Fpsx.Agent;

public static class Commands
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void Help()
    {
        Ui.Line("""
            FPSX Agent: diagnóstico + otimização compatível + medição + transparência.

            USO
              fpsx scan [--profile gaming] [--cpu-test] [--no-network] [--seconds 3] [--json]
                  Analisa o PC. Não altera nada.

              fpsx apply [ids...] [--profile gaming] [--yes] [--dry-run] [--experimental]
                  Sem ids: aplica as otimizações que o perfil seleciona automaticamente.
                  Com ids: aplica só as indicadas (ex.: startup-entry-disable:Discord).
                  Toda alteração tem backup antes e verificação depois.

              fpsx rollback --session <id> | --change <sessão>:<alteração> | --all [--force]
              fpsx history [--json]

              fpsx benchmark run --label antes [--game cs2] [--presentmon <exe>] [--duration 60] [--session <id>]
              fpsx benchmark import <arquivo.csv> --label antes [--game cs2] [--process cs2.exe]
              fpsx benchmark compare --before <id,id> --after <id,id>
              fpsx benchmark list

              fpsx catalog [--all]      Otimizações disponíveis (--all inclui as NÃO recomendadas e o porquê)
              fpsx license              Plano e licença deste PC

            PERFIS
              safe, gaming, competitive, streaming, low-end, high-end, custom
            """);
    }

    public static int Unknown(string command)
    {
        Ui.Error($"Comando desconhecido: {command}. Use 'fpsx help'.");
        return 2;
    }

    // ---------------------------------------------------------------- scan

    private static ScanResult RunScan(AgentContext ctx, Args args, bool quiet)
    {
        var options = new CollectOptions
        {
            SampleSeconds = Math.Clamp(args.Int("seconds", args.Flag("cpu-test") ? 15 : 3), 1, 120),
            CpuStressTest = args.Flag("cpu-test"),
            Network = !args.Flag("no-network"),
        };
        var snapshot = new SnapshotCollector(ctx.GameProfiles).Collect(options, quiet ? null : step => Ui.Muted($"  {step}..."));
        var engine = new DecisionEngine(ctx.Catalog, ctx.GameProfiles);
        var scan = engine.Evaluate(snapshot, args.Get("profile") ?? "gaming", ctx.License().Plan);
        File.WriteAllText(ctx.LastScanPath, JsonSerializer.Serialize(ReportBuilder.Build(scan), FpsxJson.Options));
        ctx.RecordScan(scan);
        return scan;
    }

    public static int Scan(AgentContext ctx, Args args)
    {
        var json = args.Flag("json");
        if (!json)
            Ui.Title("FPSX Scan");
        var scan = RunScan(ctx, args, quiet: json);
        var report = ReportBuilder.Build(scan);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, FpsxJson.Options));
            return 0;
        }

        PrintReport(report, scan);
        return 0;
    }

    private static void PrintReport(Report report, ScanResult scan)
    {
        Ui.Title("Hardware");
        foreach (var (k, v) in report.Hardware)
            Ui.Line($"  {k,-14} {v}");

        Ui.Title("Performance Readiness");
        foreach (var row in report.Readiness)
        {
            Console.Write("  ");
            Ui.Status(row.Status);
            Ui.Line($"  {row.Area,-14} {row.Summary}");
        }

        var issues = report.Findings.Where(f => f.Status is HealthStatus.Problem or HealthStatus.Attention or HealthStatus.Info).ToList();
        if (issues.Count > 0)
        {
            Ui.Title("Diagnóstico");
            foreach (var f in issues.OrderBy(f => f.Status switch { HealthStatus.Problem => 0, HealthStatus.Attention => 1, _ => 2 }))
            {
                Console.Write("  ");
                Ui.Status(f.Status);
                Ui.Line($"  {f.Title}");
                if (f.Detail.Length > 0)
                    Ui.Line($"            {f.Detail}");
                if (f.Recommendation is { } rec)
                    Ui.WriteLine($"            Recomendação: {rec}", ConsoleColor.Cyan);
            }
        }

        Ui.Title("Otimizações");
        foreach (var o in report.Optimizations.OrderBy(o => o.Decision switch
                 {
                     Decision.Recommended => 0, Decision.Optional => 1, Decision.Blocked => 2, Decision.AlreadyOptimal => 3, _ => 4,
                 }))
        {
            Console.Write("  ");
            Ui.Write($"{Ui.DecisionLabel(o.Decision),-14}", Ui.DecisionColor(o.Decision));
            Ui.Line($" {o.Name}  [{Ui.ClassificationLabel(o.Classification)}, risco {o.Risk.ToString().ToLowerInvariant()}]{(o.AutoSelected ? "  (perfil)" : "")}");
            Ui.Muted($"                 {o.Reason}");
            if (o.Decision is Decision.Recommended or Decision.Optional)
            {
                if (o.Potential != Potential.None || o.Classification != Classification.Troubleshooting)
                    Ui.Muted($"                 Potencial: {Ui.PotentialLabel(o.Potential)} ({o.ImpactArea})");
                if (o.Warning is { } w)
                    Ui.Warn($"                 Atenção: {w}");
                foreach (var p in o.Proposals)
                    Ui.Line($"                 > {p.Id}  {p.Title}{(p.RequiresReboot ? " (exige reinício)" : "")}{(p.Reversible ? "" : " (sem rollback)")}");
            }
        }

        Ui.Title("Resumo");
        Ui.Line($"  Problemas encontrados:     {report.ProblemsFound}");
        Ui.Line($"  Otimizações recomendadas:  {report.Recommended}");
        Ui.Line($"  Já otimizado:              {report.AlreadyOptimal}");
        Ui.Line($"  Nenhuma ação necessária:   {report.NoActionNeeded}");
        Ui.Line();
        Ui.Muted($"  Perfil {scan.ProfileId}, plano {scan.Plan}, catálogo {scan.CatalogVersion}. {(scan.Snapshot.IsElevated ? "Executando como administrador." : "Sem privilégio de administrador.")}");
        var auto = scan.Optimizations.Count(o => o.AutoSelected);
        Ui.Muted(auto > 0
            ? $"  'fpsx apply --profile {scan.ProfileId}' aplica as {auto} selecionada(s) pelo perfil, com backup e confirmação."
            : "  Nada a aplicar automaticamente neste perfil. Itens opcionais: 'fpsx apply <id>'.");
    }

    // ---------------------------------------------------------------- apply

    public static int Apply(AgentContext ctx, Args args)
    {
        var dryRun = args.Flag("dry-run");
        var assumeYes = args.Flag("yes");
        Ui.Title(dryRun ? "FPSX Apply (simulação)" : "FPSX Apply");

        // Sempre um scan novo: aplicar em cima de um diagnóstico velho é
        // aplicar sem saber o estado atual.
        var scan = RunScan(ctx, args, quiet: false);
        var explicitIds = args.Positional.Skip(1).ToList();

        List<string> selected;
        if (explicitIds.Count > 0)
        {
            // Id explícito conta como consentimento para aquele item. Um id de
            // otimização sem ":" expande para todas as propostas dela.
            selected = explicitIds.SelectMany(id =>
                scan.Optimizations.FirstOrDefault(o => o.Definition.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is { } o
                    ? o.Evaluation.Proposals.Select(p => p.Id)
                    : [id]).ToList();
        }
        else
        {
            selected = scan.Optimizations.Where(o => o.AutoSelected).SelectMany(o => o.Evaluation.Proposals.Select(p => p.Id)).ToList();
            if (selected.Count == 0)
            {
                Ui.Ok("Nada precisa ser alterado neste perfil. O FPSX prefere não mexer no que já está bom.");
                var optional = scan.Optimizations.Where(o => o.Decision == Decision.Optional).ToList();
                if (optional.Count > 0)
                    Ui.Muted($"Opcionais disponíveis: {string.Join(", ", optional.Select(o => o.Definition.Id))}. Use 'fpsx scan' para detalhes.");
                return 0;
            }
        }

        Ui.Title("Plano de alterações");
        var valid = new List<string>();
        foreach (var id in selected)
        {
            if (scan.FindProposal(id) is not { } found)
            {
                Ui.Warn($"  {id}: não encontrado no scan atual, ignorado.");
                continue;
            }

            var (result, proposal) = found;
            if (result.Decision is not (Decision.Recommended or Decision.Optional))
            {
                Ui.Warn($"  {proposal.Title}: {Ui.DecisionLabel(result.Decision)}. {result.Reason}");
                continue;
            }

            Ui.Line($"  {proposal.Title}");
            Ui.Muted($"    Classificação {Ui.ClassificationLabel(result.Definition.Classification)}, risco {result.Definition.Risk.ToString().ToLowerInvariant()}, potencial {Ui.PotentialLabel(proposal.Potential)}");
            foreach (var c in proposal.Changes)
                Ui.Muted($"    - {c.Describe()}{(c.Reversible ? "" : " (sem rollback)")}{(c.RequiresReboot ? " (exige reinício)" : "")}");
            if (result.Evaluation.Warning is { } w)
                Ui.Warn($"    Atenção: {w}");
            valid.Add(id);
        }

        if (valid.Count == 0)
        {
            Ui.Line("Nenhuma alteração válida para aplicar.");
            return 0;
        }

        if (dryRun)
        {
            Ui.Ok($"Simulação: {valid.Count} alteração(ões) seriam aplicadas. Nada foi modificado.");
            return 0;
        }

        if (!assumeYes && !Ui.Confirm($"Aplicar {valid.Count} alteração(ões)? Um backup é criado antes de cada uma."))
        {
            Ui.Line(Ui.Interactive ? "Cancelado. Nada foi alterado." : "Terminal não interativo: use --yes para confirmar. Nada foi alterado.");
            return 0;
        }

        var engine = new OptimizationEngine(new WindowsSystemAccess(ctx.GameProfiles), ctx.Store);
        var session = engine.Apply(scan, valid, new ApplyOptions
        {
            AllowExperimental = args.Flag("experimental"),
            OnFailure = failure =>
            {
                Ui.Error($"A alteração \"{failure.Description}\" falhou: {failure.Error}");
                Ui.Line("Nenhuma alteração adicional será aplicada sem sua decisão.");
                // Sem terminal: Restore, o caminho mais conservador.
                return Ui.Choose("[R]estaurar tudo desta sessão, [C]ontinuar, [X] cancelar (mantém o que já foi feito)?", "rcx", 'r') switch
                {
                    'c' => FailureChoice.Continue,
                    'x' => FailureChoice.Cancel,
                    _ => FailureChoice.Restore,
                };
            },
        });

        ctx.RecordSession(session);
        PrintSession(session);
        if (session.Changes.Any(c => c.Status == ChangeStatus.Applied && c.Applied.RequiresReboot))
            Ui.Warn("Reinicie o PC para concluir as alterações marcadas como 'exige reinício'.");
        Ui.Muted($"Sessão {session.Id}. Desfazer: fpsx rollback --session {session.Id}");
        Ui.Muted("Meça o resultado: fpsx benchmark run --label depois --session " + session.Id);
        return session.Status is SessionStatus.Completed ? 0 : 1;
    }

    private static void PrintSession(SessionRecord session)
    {
        Ui.Title($"Resultado ({session.Status})");
        foreach (var c in session.Changes)
        {
            var (label, color) = c.Status switch
            {
                ChangeStatus.Applied => ("OK       ", ConsoleColor.Green),
                ChangeStatus.RolledBack => ("DESFEITA ", ConsoleColor.Cyan),
                ChangeStatus.RollbackSkipped => ("MANTIDA  ", ConsoleColor.Yellow),
                _ => ("FALHOU   ", ConsoleColor.Red),
            };
            Console.Write("  ");
            Ui.Write(label, color);
            Ui.Line($" [{c.Id}] {c.Applied.Describe()}");
            if (c.Detail is { } d)
                Ui.Muted($"            {d}");
            if (c.Error is { } e)
                Ui.Warn($"            {e}");
        }

        foreach (var s in session.Skipped)
            Ui.Muted($"  IGNORADA  {s.ProposalId}: {s.Reason}");
    }

    // ---------------------------------------------------------------- rollback / history

    public static int Rollback(AgentContext ctx, Args args)
    {
        var force = args.Flag("force");
        var manager = new RollbackManager(new WindowsSystemAccess(ctx.GameProfiles), ctx.Store);
        Ui.Title("FPSX Rollback");

        if (args.Get("change") is { } change)
        {
            var parts = change.Split(':', 2);
            if (parts.Length != 2)
                throw new ArgumentException("Use --change <sessão>:<alteração>.");
            PrintSession(manager.RollbackChange(parts[0], parts[1], force));
            return 0;
        }

        if (args.Get("session") is { } sessionId)
        {
            PrintSession(manager.RollbackSession(sessionId, force));
            return 0;
        }

        if (args.Flag("all"))
        {
            if (!args.Flag("yes") && !Ui.Confirm("Desfazer TODAS as alterações já feitas pelo FPSX neste PC?"))
                return 0;
            foreach (var s in manager.RollbackAll(force))
                PrintSession(s);
            return 0;
        }

        throw new ArgumentException("Informe --session, --change ou --all.");
    }

    public static int History(AgentContext ctx, Args args)
    {
        if (!Gate(ctx, Feature.History))
            return 3;
        var sessions = ctx.Store.All();
        if (args.Flag("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(sessions, FpsxJson.Options));
            return 0;
        }

        Ui.Title("Histórico");
        if (sessions.Count == 0)
        {
            Ui.Line("  Nenhuma sessão de otimização neste PC.");
            return 0;
        }

        var runs = LoadRuns(ctx).ToDictionary(r => r.Id);
        foreach (var s in sessions)
        {
            var applied = s.Changes.Count(c => c.Status == ChangeStatus.Applied);
            var rolled = s.Changes.Count(c => c.Status == ChangeStatus.RolledBack);
            Ui.Line($"  {s.StartedAt:dd/MM/yyyy HH:mm}  {s.Id}  perfil {s.ProfileId}  {s.Status}");
            Ui.Muted($"      {applied} alteração(ões) ativas, {rolled} desfeitas, {s.AlreadyOptimal.Count} já estavam corretas, {s.Skipped.Count} ignoradas");
            var before = runs.Values.Where(r => r.SessionId == s.Id && r.Label.StartsWith("antes", StringComparison.OrdinalIgnoreCase)).ToList();
            var after = runs.Values.Where(r => r.SessionId == s.Id && !r.Label.StartsWith("antes", StringComparison.OrdinalIgnoreCase)).ToList();
            if (before.Count > 0 && after.Count > 0)
            {
                var cmp = BenchmarkComparer.Compare(before.Select(r => r.Stats).ToList(), after.Select(r => r.Stats).ToList());
                Ui.Muted($"      Benchmark: {cmp.Verdict}");
            }
        }

        return 0;
    }

    // ---------------------------------------------------------------- benchmark

    /// <summary>Recurso fora do plano: explica qual plano libera, sem erro técnico.</summary>
    private static bool Gate(AgentContext ctx, Feature feature)
    {
        var plan = ctx.License().Plan;
        if (PlanFeatures.Allows(plan, feature))
            return true;
        Ui.Warn($"{PlanFeatures.Label(feature)} está disponível a partir do plano {PlanFeatures.RequiredPlan(feature)}. Seu plano: {plan}.");
        return false;
    }

    /// <summary>
    /// Mede as partidas no terminal até Ctrl+C. --process mede um executável
    /// qualquer (teste do próprio monitor, ou jogo ainda sem perfil).
    /// </summary>
    public static int Monitor(AgentContext ctx, Args args)
    {
        var profiles = ctx.GameProfiles;
        if (args.Get("process") is { } exe)
        {
            var name = args.Get("name") ?? exe;
            profiles = [new GameProfile { Id = "custom", Name = name, Benchmark = new GameBenchmarkSpec { Process = exe } }];
        }

        using var monitor = new GameplayMonitor(profiles, ctx.PresentMonPath, Path.Combine(ctx.DataDir, "gameplay-tmp"), AgentContext.Version);
        var store = ctx.Gameplay;
        monitor.StatusChanged += s => Ui.Muted($"  {DateTime.Now:HH:mm:ss}  {s}");
        monitor.Recorded += s =>
        {
            store.Save(s);
            Ui.Ok($"  Registrada: {s.GameName}, {s.MeasuredSeconds / 60:0.#} min, {FormatStats(s.Stats)}");
        };
        using var done = new ManualResetEventSlim();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            done.Set();
        };
        Ui.Title("FPSX: medição automática das partidas");
        Ui.Muted("  Jogos: " + string.Join(", ", profiles.Select(p => p.Name)) + ". Ctrl+C para parar.");
        monitor.Start();
        if (!monitor.Running)
            return 1;
        done.Wait();
        return 0;
    }

    public static int Gameplay(AgentContext ctx, Args args)
    {
        var sessions = ctx.Gameplay.All();
        Ui.Title("Partidas medidas");
        if (sessions.Count == 0)
            Ui.Muted("  Nenhuma partida ainda. Deixe o FPSX aberto e jogue: a medição é automática.");
        foreach (var s in sessions)
            Ui.Line($"  {s.StartedAt:dd/MM HH:mm}  {s.GameName,-18} {s.MeasuredSeconds / 60,5:0.#} min  {FormatStats(s.Stats)}");

        var pivot = GameplayComparer.Pivots(ctx.Store.All()).FirstOrDefault();
        var game = args.Get("game") ?? sessions.FirstOrDefault()?.GameId;
        if (pivot is null || game is null)
            return 0;
        var gameName = sessions.FirstOrDefault(s => s.GameId == game)?.GameName ?? game;
        var cmp = GameplayComparer.Compare(sessions, game, gameName, pivot.StartedAt, $"otimização de {pivot.StartedAt:dd/MM HH:mm}");
        Ui.Title($"Antes e depois da {cmp.PivotLabel}");
        Ui.Muted("  " + cmp.Status);
        if (cmp.Result is { } r)
        {
            foreach (var m in r.Metrics)
                Ui.Line(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  {0,-16} {1,8:0.0} -> {2,8:0.0}  ({3}{4:0.0}%){5}",
                    m.Metric, m.Before, m.After, m.DeltaPercent >= 0 ? "+" : "", m.DeltaPercent, m.Significant ? "" : "  dentro da variação normal"));
            Ui.Line("  " + r.Verdict);
            foreach (var w in r.Warnings)
                Ui.Muted("  " + w);
        }

        return 0;
    }

    public static int Benchmark(AgentContext ctx, Args args)
    {
        if (!Gate(ctx, Feature.Benchmark))
            return 3;
        var sub = args.Positional.Skip(1).FirstOrDefault()?.ToLowerInvariant();
        Directory.CreateDirectory(ctx.BenchmarksDir);
        switch (sub)
        {
            case "run":
                return BenchmarkRun(ctx, args);
            case "import":
                return BenchmarkImport(ctx, args);
            case "compare":
                return BenchmarkCompare(ctx, args);
            case "list":
                Ui.Title("Benchmarks");
                foreach (var r in LoadRuns(ctx).OrderByDescending(r => r.At))
                    Ui.Line($"  {r.Id}  {r.At:dd/MM HH:mm}  {r.Label,-10} {r.GameId,-6} {FormatStats(r.Stats)}");
                return 0;
            default:
                throw new ArgumentException("Use: fpsx benchmark run | import | compare | list");
        }
    }

    private static int BenchmarkRun(AgentContext ctx, Args args)
    {
        var gameId = args.Get("game") ?? "cs2";
        var profile = ctx.GameProfiles.FirstOrDefault(p => p.Id == gameId) ?? throw new ArgumentException($"Perfil de jogo desconhecido: {gameId}");
        var presentMon = args.Get("presentmon") ?? Environment.GetEnvironmentVariable("FPSX_PRESENTMON")
                         ?? Path.Combine(AppContext.BaseDirectory, "tools", "PresentMon.exe");
        var duration = Math.Clamp(args.Int("duration", profile.Benchmark.DurationSeconds), 10, 600);
        var label = args.Get("label") ?? "captura";

        Ui.Title($"FPSX Benchmark: {profile.Name}");
        Ui.Muted($"  Método: {profile.Benchmark.Method}");
        Ui.Muted($"  Recomendado: {profile.Benchmark.RecommendedRuns} rodadas antes e {profile.Benchmark.RecommendedRuns} depois, no mesmo cenário.");

        var id = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", Inv);
        var csv = Path.Combine(ctx.BenchmarksDir, $"{id}-{label}.csv");
        var (run, _) = BenchmarkRunner.Capture(new BenchmarkRequest(presentMon, profile.Benchmark.Process, duration, csv), label, gameId, s => Ui.Muted($"  {s}..."));
        run = run with { SessionId = args.Get("session") };
        SaveRun(ctx, run);
        PrintRun(run);
        return 0;
    }

    private static int BenchmarkImport(AgentContext ctx, Args args)
    {
        var file = args.Positional.Skip(2).FirstOrDefault() ?? throw new ArgumentException("Informe o CSV do PresentMon.");
        var gameId = args.Get("game") ?? "cs2";
        var process = args.Get("process") ?? ctx.GameProfiles.FirstOrDefault(p => p.Id == gameId)?.Benchmark.Process;
        var frametimes = PresentMonCsv.ReadFrametimes(File.ReadAllText(file), process);
        var run = new Fpsx.Core.Benchmark.BenchmarkRun
        {
            Id = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", Inv) + "-" + Guid.NewGuid().ToString("N")[..4],
            Label = args.Get("label") ?? "importado",
            GameId = gameId,
            Process = process ?? "",
            At = DateTimeOffset.Now,
            SessionId = args.Get("session"),
            Stats = FrameStats.From(frametimes),
            SourceCsv = Path.GetFullPath(file),
        };
        SaveRun(ctx, run);
        PrintRun(run);
        return 0;
    }

    private static int BenchmarkCompare(AgentContext ctx, Args args)
    {
        var runs = LoadRuns(ctx).ToDictionary(r => r.Id);
        List<Fpsx.Core.Benchmark.BenchmarkRun> Pick(string option) =>
            (args.Get(option) ?? throw new ArgumentException($"Informe --{option} <id,id>."))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => runs.TryGetValue(id, out var r) ? r : throw new ArgumentException($"Benchmark não encontrado: {id}"))
            .ToList();

        var before = Pick("before");
        var after = Pick("after");
        var cmp = BenchmarkComparer.Compare(before.Select(r => r.Stats).ToList(), after.Select(r => r.Stats).ToList());

        Ui.Title("Resultado");
        foreach (var m in cmp.Metrics)
        {
            var color = m.Improved ? ConsoleColor.Green : m.Worsened ? ConsoleColor.Red : ConsoleColor.Gray;
            Ui.Write($"  {m.Metric,-16} {m.Before,8:0.0} -> {m.After,8:0.0}   {(m.DeltaPercent >= 0 ? "+" : "")}{m.DeltaPercent:0.0}%", color);
            Ui.Line(m.Significant ? "" : "  (dentro da variação normal)");
        }

        Ui.Line();
        Ui.Line("  " + cmp.Verdict);
        foreach (var w in cmp.Warnings)
            Ui.Warn("  " + w);
        Ui.Muted("  " + cmp.Method);
        return 0;
    }

    private static string FormatStats(FrameStats s) =>
        $"FPS {s.AvgFps:0.0}  1% {s.Low1Fps:0.0}  0.1% {s.Low01Fps:0.0}  frametime {s.AvgFrametimeMs:0.00} ms  stutter {s.StutterCount}";

    private static void PrintRun(Fpsx.Core.Benchmark.BenchmarkRun run)
    {
        var s = run.Stats;
        Ui.Title($"Benchmark {run.Id} ({run.Label})");
        Ui.Line($"  Average FPS:     {s.AvgFps:0.0}");
        Ui.Line($"  1% Low:          {s.Low1Fps:0.0}");
        Ui.Line($"  0.1% Low:        {s.Low01Fps:0.0}");
        Ui.Line($"  Frametime médio: {s.AvgFrametimeMs:0.00} ms (P99 {s.P99FrametimeMs:0.00} ms, máx {s.MaxFrametimeMs:0.0} ms)");
        Ui.Line($"  Stutters:        {s.StutterCount} ({s.StuttersPerMinute:0.0}/min)");
        Ui.Line($"  Quadros:         {s.Frames} em {s.DurationSeconds:0.0} s");
        if (run.AvgCpuPercent is { } c)
            Ui.Line($"  CPU/GPU/RAM:     {c:0}% / {(run.AvgGpuPercent is { } g ? g.ToString("0", Inv) + "%" : "?")} / {(run.AvgRamPercent is { } r ? r.ToString("0", Inv) + "%" : "?")}");
        if (!s.Sufficient)
            Ui.Warn("  Captura curta demais para um resultado confiável (mínimo 10 s e 300 quadros).");
    }

    private static void SaveRun(AgentContext ctx, Fpsx.Core.Benchmark.BenchmarkRun run) =>
        File.WriteAllText(Path.Combine(ctx.BenchmarksDir, run.Id + ".json"), JsonSerializer.Serialize(run, FpsxJson.Options));

    private static IEnumerable<Fpsx.Core.Benchmark.BenchmarkRun> LoadRuns(AgentContext ctx) =>
        Directory.Exists(ctx.BenchmarksDir)
            ? Directory.EnumerateFiles(ctx.BenchmarksDir, "*.json")
                .Select(f => JsonSerializer.Deserialize<Fpsx.Core.Benchmark.BenchmarkRun>(File.ReadAllText(f), FpsxJson.Options))
                .OfType<Fpsx.Core.Benchmark.BenchmarkRun>()
            : [];

    // ---------------------------------------------------------------- catálogo / licença

    public static int CatalogList(AgentContext ctx, Args args)
    {
        Ui.Title($"Catálogo {ctx.Catalog.Version}");
        foreach (var o in ctx.Catalog.Optimizations.Where(o => args.Flag("all") || o.Classification != Classification.NotRecommended))
        {
            Ui.Line($"  {o.Id,-44} {Ui.ClassificationLabel(o.Classification),-16} risco {o.Risk.ToString().ToLowerInvariant(),-7} plano {o.MinPlan}");
            Ui.Muted($"      {o.Description}");
            if (o.Classification == Classification.NotRecommended)
                Ui.Warn($"      Por que não aplicamos: {o.Justification.DoesNotWorkOn} Risco: {o.Justification.Risk}");
        }

        if (!args.Flag("all"))
            Ui.Muted("  Use --all para ver os tweaks populares que o FPSX NÃO aplica, e por quê.");
        return 0;
    }

    public static int License(AgentContext ctx)
    {
        var l = ctx.License();
        Ui.Title("Licença");
        Ui.Line($"  Conta:    {l.Email ?? "não conectada (fpsx login <email>)"}");
        Ui.Line($"  Plano:    {l.Plan} ({l.Status})");
        Ui.Line($"  Validade: {(l.ExpiresAt is { } e ? e.ToLocalTime().ToString("dd/MM/yyyy", Inv) : "sem vencimento")}");
        if (l.ValidUntil is { } v)
            Ui.Muted($"  Funciona offline até {v.ToLocalTime():dd/MM/yyyy HH:mm}.");
        if (l.Notice is { } n)
            Ui.Warn($"  {n}");
        Ui.Line();
        Ui.Line("  Liberado no seu plano:");
        foreach (var f in Enum.GetValues<Feature>())
            Ui.Line($"    {(PlanFeatures.Allows(l.Plan, f) ? "[x]" : "[ ]")} {PlanFeatures.Label(f)}{(PlanFeatures.Allows(l.Plan, f) ? "" : $" (plano {PlanFeatures.RequiredPlan(f)})")}");
        return 0;
    }

    public static async Task<int> Login(AgentContext ctx, Args args)
    {
        var email = args.Positional.Skip(1).FirstOrDefault() ?? throw new ArgumentException("Use: fpsx login <email>");
        Console.Write("Senha: ");
        var password = ReadHidden();
        try
        {
            var l = await ctx.LoginAsync(email, password, Environment.OSVersion.Version.Build);
            Ui.Ok($"Conectado como {l.Email}. Plano {l.Plan}.");
            return 0;
        }
        catch (ApiException ex) when (ex.Detail is { } d && d.TryGetProperty("devices", out var devices))
        {
            Ui.Error(ex.Message);
            foreach (var dev in devices.EnumerateArray())
                Ui.Line($"  - {dev.GetProperty("name").GetString()} (visto em {dev.GetProperty("last_seen_at").GetString()})");
            Ui.Muted("  Desative um PC em fpsx.app/conta e rode o login de novo.");
            return 1;
        }
    }

    public static async Task<int> Logout(AgentContext ctx)
    {
        await ctx.LogoutAsync();
        Ui.Ok("Desconectado. Este PC liberou a vaga na sua conta e voltou ao plano Free.");
        return 0;
    }

    public static async Task<int> Sync(AgentContext ctx)
    {
        var r = await ctx.SyncAsync(Environment.OSVersion.Version.Build);
        if (r.Message is { } m)
            Ui.Warn(m);
        else if (r.Online)
            Ui.Ok("Licença, catálogo e telemetria sincronizados.");
        else
            Ui.Muted("Nenhuma conta conectada neste PC.");
        return 0;
    }

    private static string ReadHidden()
    {
        // Via pipe (instalador, scripts), o PowerShell 5.1 manda um BOM invisível
        // na frente: sem tirar, a senha certa vira "senha incorreta".
        if (Console.IsInputRedirected)
            return (Console.ReadLine() ?? "").Trim('﻿', '​', '\r', '\n');
        var sb = new System.Text.StringBuilder();
        ConsoleKeyInfo key;
        while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace && sb.Length > 0)
                sb.Length--;
            else if (!char.IsControl(key.KeyChar))
                sb.Append(key.KeyChar);
        }

        Console.WriteLine();
        return sb.ToString();
    }
}
