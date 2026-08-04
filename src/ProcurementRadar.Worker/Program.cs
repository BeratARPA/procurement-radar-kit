using ProcurementRadar.Worker;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// D-07: compact JSON on the console, daily rolling file on disk. Sinks and
// levels live in appsettings.json so they can be changed without a rebuild.
// The default providers are cleared first, otherwise every line is written
// twice: once by Serilog and once by the built-in console logger.
builder.Logging.ClearProviders();
builder.Services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services));

builder.Services.AddHostedService<PollingWorker>();

using var host = builder.Build();

try
{
    host.Run();
    return 0;
}
catch (Exception exception)
{
    Log.Fatal(exception, "Worker terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
