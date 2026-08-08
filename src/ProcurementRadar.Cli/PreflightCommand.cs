using Microsoft.Extensions.Logging.Abstractions;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Settings;
using ProcurementRadar.Notifier;
using ProcurementRadar.Parsers;
using ProcurementRadar.Persistence;
using ProcurementRadar.Scoring;
using Serilog;

namespace ProcurementRadar.Cli;

/// <summary>
/// Ten seconds against the real database, run at every deploy.
/// </summary>
/// <remarks>
/// D-17. Every production breakage in this project came from the same gap: a
/// unit test with a fake behind the interface proves the code and not the
/// driver. Two records that Dapper could not materialize, a scalar it could not
/// convert, a parameter nothing supplied, an alarm whose message was not in the
/// row the worker reads - all green in dotnet test, all broken in production,
/// all of them found hours later by reading a log.
///
/// This is not an integration test suite and deliberately not part of dotnet
/// test: tests that need a database on a project with no CI become tests nobody
/// runs. It is a command the install script calls, and a deploy that fails it
/// stops there rather than two hours later.
/// </remarks>
internal static class PreflightCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var connectionString = CliConfiguration.ConnectionString();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Log.Error("PR_DB_CONNECTION_STRING is not set. See docs/decisions.md D-06.");
            return 1;
        }

        PersistenceSetup.RegisterTypeHandlers();

        await using var connectionFactory = new NpgsqlConnectionFactory(connectionString);
        var settings = new SettingRepository(connectionFactory, TimeProvider.System);
        var problems = new List<string>();

        // 1. Every read path, against real Npgsql. This is where a record Dapper
        //    cannot materialize shows itself, and it has twice.
        await CheckAsync(problems, "source.list", async () =>
            await new SourceRepository(connectionFactory, TimeProvider.System).ListAllAsync(cancellationToken).ConfigureAwait(false));

        await CheckAsync(problems, "notice.commands", async () =>
        {
            var queries = new CommandQueries(connectionFactory);

            var open = await queries.ListOpenAsync(0, 1, cancellationToken).ConfigureAwait(false);
            await queries.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            await queries.ListForPreviewAsync(1, cancellationToken).ConfigureAwait(false);

            // The reads the interface added (T-19). Search and closing were
            // already written and had never run outside Telegram; the detail by
            // id is new SQL, and new SQL that only a screen calls is exactly the
            // kind that is found broken by a person looking at it.
            await queries.SearchAsync("a", 1, cancellationToken).ConfigureAwait(false);
            await queries.ListClosingSoonAsync(48, 1, cancellationToken).ConfigureAwait(false);

            // Against a row that exists where there is one. Asking for id 0 would
            // return nothing and prove only that the statement parses - the
            // materialisation is the half that has broken here before.
            await queries
                .GetDetailByIdAsync(open.Count > 0 ? open[0].NoticeId : 0, cancellationToken)
                .ConfigureAwait(false);

            return true;
        });

        await CheckAsync(problems, "award.list", async () =>
            await new AwardRepository(connectionFactory, TimeProvider.System)
                .ListRecentAsync(1, cancellationToken).ConfigureAwait(false));

        // 1b. An active source with no selectors can only fail. Eleven of them
        //     were switched on at once and spent an evening fetching pages
        //     nothing could read - one of them thirty-three times - and the only
        //     sign was a warning in a log nobody was reading yet.
        await CheckAsync(problems, "source.configured", async () =>
        {
            var sources = await new SourceRepository(connectionFactory, TimeProvider.System)
                .ListActiveAsync(cancellationToken)
                .ConfigureAwait(false);

            var unconfigured = sources
                .Where(source => source.AdapterKey == "generic_table")
                .Where(source => string.IsNullOrWhiteSpace(source.AdapterConfig)
                    || source.AdapterConfig.Trim() is "{}" or "null")
                .Select(source => source.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .ToList();

            return unconfigured.Count == 0
                ? true
                : throw new InvalidOperationException(
                    "active sources with no adapter_config: " + string.Join(", ", unconfigured)
                    + ". Measure one with try-source or switch it off.");
        });

        // 2. The scalar the settings refresher reads every thirty seconds. It
        //    threw for half an hour before anybody noticed, because @ is
        //    PostgreSQL's absolute-value operator and the error named a column.
        await CheckAsync(problems, "setting.version", async () =>
            await settings.GetVersionAsync(cancellationToken).ConfigureAwait(false));

        // 3. Every setting, loaded from the row with the loader the worker uses.
        //    A template that is in the file and not in the table is invisible to
        //    every test, because the test loads the file.
        var stored = await settings.ListAsync(cancellationToken).ConfigureAwait(false);
        var byKey = stored.ToDictionary(setting => setting.Key, setting => setting.Value, StringComparer.Ordinal);

        foreach (var (key, fileName) in SettingKeys.Seeds)
        {
            if (!byKey.TryGetValue(key, out var json))
            {
                problems.Add($"setting '{key}' is not in the table; the worker is running on {fileName}.");
                continue;
            }

            await CheckAsync(problems, $"setting.{key}", () => Task.FromResult(Load(key, json)));
        }

        // 4. What the interface will show against what the repository holds.
        //    Not a fault on its own - the table wins by design - but it is how a
        //    config edit does nothing and looks like it worked.
        var configDirectory = ConfigDirectory.Find(ReadOption(args, "--config-dir"));

        foreach (var (key, fileName) in SettingKeys.Seeds)
        {
            if (!byKey.TryGetValue(key, out var json)) continue;

            var file = ConfigDirectory.Read(configDirectory, fileName);

            if (!SettingComparison.SameJson(json, file))
            {
                Log.Warning(
                    "{Key} differs from config/{FileName}. The table is what runs. "
                    + "Push the file through the api if it is the one you meant.",
                    key,
                    fileName);
            }
        }

        if (problems.Count > 0)
        {
            foreach (var problem in problems)
            {
                Log.Error("{Problem}", problem);
            }

            Log.Fatal("Preflight failed with {Count} problem(s). Do not deploy this.", problems.Count);

            return 1;
        }

        Log.Information(
            "Preflight passed: every read path, the version scalar and all {Count} settings load against the real database",
            SettingKeys.Seeds.Count);

        return 0;
    }

    private static object Load(string key, string json) => key switch
    {
        SettingKeys.Keywords => KeywordRules.Load(json),
        SettingKeys.Messages => MessageTemplates.Load(json),
        SettingKeys.DatePatterns => TurkishDatePatterns.Load(json),
        SettingKeys.Normalization => Domain.Normalization.NormalizationRules.Load(json),
        SettingKeys.PersonalData => Domain.Normalization.PersonalDataRules.Load(json),
        SettingKeys.AwardLabels => AwardLabels.Load(json),
        SettingKeys.Politeness => PolitenessSettings.Load(json),
        _ => json
    };

    private static async Task CheckAsync<T>(List<string> problems, string name, Func<Task<T>> check)
    {
        try
        {
            await check().ConfigureAwait(false);

            Log.Information("  {Name}: ok", name);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            problems.Add($"{name}: {exception.Message}");
        }
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
