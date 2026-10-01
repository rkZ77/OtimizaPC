using System.Text;
using Rkzfps.Agent;
using Rkzfps.Client;

Console.OutputEncoding = Encoding.UTF8;

var valueOptions = new[] { "profile", "seconds", "session", "change", "label", "game", "presentmon", "duration", "before", "after", "process", "name", "version" };
Args cli;
try
{
    cli = new Args(args, valueOptions);
}
catch (ArgumentException ex)
{
    Ui.Error(ex.Message);
    return 2;
}

var command = cli.Positional.FirstOrDefault()?.ToLowerInvariant();
if (command is null or "help" or "ajuda" || cli.Flag("help"))
{
    Commands.Help();
    return 0;
}

try
{
    var ctx = new AgentContext();
    return command switch
    {
        "scan" => Commands.Scan(ctx, cli),
        "apply" => Commands.Apply(ctx, cli),
        "rollback" => Commands.Rollback(ctx, cli),
        "history" => Commands.History(ctx, cli),
        "benchmark" => Commands.Benchmark(ctx, cli),
        "monitor" => Commands.Monitor(ctx, cli),
        "gameplay" => Commands.Gameplay(ctx, cli),
        "sensors" => Commands.Sensors(),
        "update" => await Commands.Update(ctx, cli),
        "catalog" => Commands.CatalogList(ctx, cli),
        "license" => Commands.License(ctx),
        "login" => await Commands.Login(ctx, cli),
        "logout" => await Commands.Logout(ctx),
        "sync" => await Commands.Sync(ctx),
        _ => Commands.Unknown(command),
    };
}
catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or ArgumentException or InvalidOperationException or Rkzfps.Core.Engine.SafetyViolationException or ApiException)
{
    // Erro esperado (catálogo inválido, PresentMon ausente, sessão que não
    // existe): mensagem limpa, sem stack trace.
    Ui.Error(ex.Message);
    return 1;
}
