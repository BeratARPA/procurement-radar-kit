using ProcurementRadar.Cli;
using Serilog;
using Serilog.Formatting.Compact;

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
        + "seed [--file <path>] | reparse [--source-id <id>] [--batch-size <n>] [--config-dir <path>]",
        problem);

    return 1;
}
