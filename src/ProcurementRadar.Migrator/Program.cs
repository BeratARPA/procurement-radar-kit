using System.Reflection;
using DbUp;
using Microsoft.Extensions.Configuration;
using ProcurementRadar.Migrator;
using Serilog;
using Serilog.Formatting.Compact;

// D-07: compact JSON on the console, daily rolling file on disk. The migrator
// configures Serilog in code rather than from appsettings.json because it is a
// one-shot command with nothing worth tuning at run time.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.WithProperty("Application", "ProcurementRadar.Migrator")
    .WriteTo.Console(new CompactJsonFormatter())
    .WriteTo.File(
        new CompactJsonFormatter(),
        "logs/procurement-radar-migrator-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .CreateLogger();

try
{
    // D-06: user secrets while developing, environment variables when running
    // for real (a shell that sourced .env, or a systemd EnvironmentFile).
    // Environment variables win so the host can always override the developer.
    var configuration = new ConfigurationBuilder()
        .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
        .AddEnvironmentVariables()
        .Build();

    var connectionString = configuration["PR_DB_CONNECTION_STRING"];
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Log.Error(
            "PR_DB_CONNECTION_STRING is not set. Set it as an environment variable, "
            + "or run: dotnet user-secrets set PR_DB_CONNECTION_STRING \"...\" "
            + "--project src/ProcurementRadar.Migrator. See docs/decisions.md D-06.");
        return 1;
    }

    var upgrader = DeployChanges.To
        .PostgresqlDatabase(connectionString)
        .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
        // DDL is transactional in PostgreSQL: a script that fails halfway leaves
        // the schema exactly as it was instead of half migrated.
        .WithTransactionPerScript()
        .LogTo(new SerilogUpgradeLog())
        .Build();

    if (!upgrader.IsUpgradeRequired())
    {
        Log.Information("Database is already up to date, nothing to apply");
        return 0;
    }

    var result = upgrader.PerformUpgrade();

    if (!result.Successful)
    {
        Log.Error(result.Error, "Migration failed on script {ScriptName}", result.ErrorScript?.Name);
        return 1;
    }

    foreach (var script in result.Scripts)
    {
        Log.Information("Applied {ScriptName}", script.Name);
    }

    Log.Information("Migration finished, {ScriptCount} script(s) applied", result.Scripts.Count());
    return 0;
}
catch (Exception exception)
{
    Log.Fatal(exception, "Migrator terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
