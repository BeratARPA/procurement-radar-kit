using System.Text.Json;
using ProcurementRadar.Persistence;
using Serilog;

namespace ProcurementRadar.Cli;

/// <summary>
/// Loads config/sources.seed.json into the <c>source</c> table. Safe to run as
/// often as you like: a url that is already registered has its definition
/// refreshed rather than duplicated.
/// </summary>
internal static class SeedCommand
{
    private const string DefaultPath = "config/sources.seed.json";

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(ReadOption(args, "--file") ?? DefaultPath);

        if (!File.Exists(path))
        {
            Log.Error(
                "Seed file not found at {Path}. Run from the repository root, or pass --file <path>.",
                path);
            return 1;
        }

        var connectionString = CliConfiguration.ConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Log.Error(
                "PR_DB_CONNECTION_STRING is not set. Set it as an environment variable, or run: "
                + "dotnet user-secrets set PR_DB_CONNECTION_STRING \"...\" --project src/ProcurementRadar.Cli. "
                + "See docs/decisions.md D-06.");
            return 1;
        }

        SeedPlan plan;
        try
        {
            plan = SourceSeedReader.Read(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
        }
        catch (JsonException exception)
        {
            Log.Error(exception, "Could not read the seed file at {Path}", path);
            return 1;
        }

        foreach (var rejection in plan.Rejections)
        {
            Log.Warning("Rejected a seed entry: {Reason}", rejection);
        }

        PersistenceSetup.RegisterTypeHandlers();

        await using var connectionFactory = new NpgsqlConnectionFactory(connectionString);
        var repository = new SourceRepository(connectionFactory, TimeProvider.System);

        var inserted = 0;
        var updated = 0;

        foreach (var source in plan.Sources)
        {
            if (await repository.UpsertAsync(source, cancellationToken).ConfigureAwait(false))
            {
                inserted++;
                Log.Information("Registered {AuthorityName} at {Url}", source.AuthorityName, source.Url);
            }
            else
            {
                updated++;
            }
        }

        Log.Information(
            "Seed finished from {Path}: {Inserted} added, {Updated} refreshed, "
            + "{Placeholders} skipped for a TODO url, {Rejected} rejected",
            path,
            inserted,
            updated,
            plan.SkippedPlaceholders,
            plan.Rejections.Count);

        return 0;
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
