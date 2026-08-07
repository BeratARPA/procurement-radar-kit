using System.Text;
using ProcurementRadar.Cli;
using Serilog;
using Serilog.Formatting.Compact;

// The sinks below write JSON so the logs can be grepped and fed to tools (D-07).
// On Windows the console defaults to the system code page, and .NET's encoder
// for a code page uses best-fit substitution: a Turkish “ becomes a plain ", and
// a notice title containing one turns the log line into invalid JSON. The letters
// go too - ş, ğ and İ all arrive as question marks. The file sink is unaffected,
// which is what makes it easy to miss.
Console.OutputEncoding = Encoding.UTF8;

// D-07. Configured in code rather than from appsettings.json because this is a
// short-lived command with nothing worth tuning at run time. The worker, which
// runs for weeks at a time, reads its configuration from a file instead.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.WithProperty("Application", "ProcurementRadar.Cli")
    .WriteTo.Console(new CompactJsonFormatter())
    .WriteTo.File(
        new CompactJsonFormatter(),
        "logs/procurement-radar-cli-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .CreateLogger();

// D-06: the same .env the worker reads, so the cli behaves identically whether
// it is run from a shell that exported the variables or from one that did not.
ProcurementRadar.Collector.DotEnv.Load();

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    // Let the command unwind and close its connection rather than being killed
    // halfway through a write.
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

try
{
    return (args.Length > 0 ? args[0] : null) switch
    {
        "seed" => await SeedCommand.RunAsync(args[1..], shutdown.Token),
        "reparse" => await ReparseCommand.RunAsync(args[1..], shutdown.Token),
        "check-source" => await CheckSourceCommand.RunAsync(args[1..], shutdown.Token),
        "telegram-check" => await TelegramCheckCommand.RunAsync(args[1..], shutdown.Token),
        "ekap-probe" => await EkapProbeCommand.RunAsync(args[1..], shutdown.Token),
        "ekap-ilan" => await EkapAnnouncementCommand.RunAsync(args[1..], shutdown.Token),
        "award-probe" => await AwardProbeCommand.RunAsync(args[1..], shutdown.Token),
        "preflight" => await PreflightCommand.RunAsync(args[1..], shutdown.Token),
        null => Usage("no command given"),
        var unknown => Usage($"unknown command '{unknown}'")
    };
}
catch (OperationCanceledException)
{
    Log.Warning("Cancelled");
    return 130;
}
catch (Exception exception)
{
    Log.Fatal(exception, "Command failed");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

static int Usage(string problem)
{
    Log.Error(
        "{Problem}. Usage: dotnet run --project src/ProcurementRadar.Cli -- "
        + "seed [--file <path>] | reparse [--source-id <id>] [--batch-size <n>] [--config-dir <path>] | "
        + "check-source --html <path> --config <path> [--url <url>] [--charset <name>] | "
        + "telegram-check [--send] | ekap-probe [--url <u>] [--search <s>] [--button <name>] | "
        + "ekap-ilan [--limit <n>] | award-probe --ikn <ikn> | preflight",
        problem);

    return 1;
}
