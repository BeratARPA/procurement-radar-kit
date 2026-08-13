using System.Text.Json;
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

            // Its own SQL, not the preview's, because it joins notice_score and
            // the preview deliberately does not. A join that only the rescore
            // button calls is exactly the kind that goes unnoticed until someone
            // presses it.
            await queries.ListForRescoreAsync(1, cancellationToken).ConfigureAwait(false);

            // The reads the interface added (T-19). Search and closing were
            // already written and had never run outside Telegram; the detail by
            // id is new SQL, and new SQL that only a screen calls is exactly the
            // kind that is found broken by a person looking at it.
            await queries.SearchAsync("a", 1, cancellationToken).ConfigureAwait(false);
            await queries.ListAllAsync(0, 1, cancellationToken).ConfigureAwait(false);
            await queries.ListClosingSoonAsync(48, 1, cancellationToken).ConfigureAwait(false);

            // Against a row that exists where there is one. Asking for id 0 would
            // return nothing and prove only that the statement parses - the
            // materialisation is the half that has broken here before.
            await queries
                .GetDetailByIdAsync(open.Count > 0 ? open[0].NoticeId : 0, cancellationToken)
                .ConfigureAwait(false);

            return true;
        });

        // The source screen's two writes (T-21), rolled back. A statement only a
        // screen calls is exactly the kind that is found broken by a person
        // looking at it - and these two carry the rule that editing a selector
        // must not touch the health columns.
        await CheckAsync(problems, "source.definition", async () =>
        {
            var sources = new SourceRepository(connectionFactory, TimeProvider.System);
            var existing = await sources.ListAllAsync(cancellationToken).ConfigureAwait(false);

            if (existing.Count == 0) return true;

            var sample = existing[0];

            // Written back exactly as it is: the statement runs, nothing moves.
            var updated = await sources
                .UpdateDefinitionAsync(sample.Id, sample, cancellationToken)
                .ConfigureAwait(false);

            return updated
                ? true
                : throw new InvalidOperationException(
                    $"UpdateDefinitionAsync did not find source {sample.Id}.");
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

        // 5. And the copy the worker will actually read.
        //
        //    This check used to look only at the repository, which is the one
        //    directory the running system never opens: install-windows.ps1
        //    publishes both exes to publish/ and copies config beside them, so
        //    a deployed worker reads publish/config. Anyone who runs `dotnet
        //    publish` by hand updates the exes and leaves that copy behind.
        //
        //    It is not a fault while the table is populated - the table wins by
        //    design (D-16) and the seeder does not overwrite. It becomes one the
        //    day the table is emptied and reseeded, because it would silently
        //    reseed from a list nobody has used for weeks. CLAUDE.md already
        //    lists this failure; on 9 August 2026 it happened again, with the
        //    published keywords twenty-six terms behind the repository.
        CheckDeployedConfig(configDirectory);

        // 6. And the schema the interface was generated from.
        //
        //    D-16 has the Dart client generated from the api's own document
        //    rather than hand-written, which removes a whole class of drift and
        //    creates one: generating is now a step, and a step can be skipped.
        //
        //    It was, for two days. The api grew ActiveChangeRequest.reason on
        //    11 August, the client was not regenerated, and the switch on the
        //    Kaynaklar screen went on sending a body without it. The field is
        //    optional, so nothing returned an error - the api simply read the
        //    absent reason as `manual`, which is one of the three that are never
        //    re-probed. Every source switched off from the interface opted out
        //    of D-22 quietly.
        await CheckGeneratedClientAsync(cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// Warns when publish/config has fallen behind the repository.
    /// </summary>
    /// <remarks>
    /// Only reports; the deployed copy being stale does not stop a deploy, it
    /// makes the next reseed restore something nobody meant. Nothing is said at
    /// all when there is no publish/ - a developer machine without one is the
    /// normal case, not a finding.
    /// </remarks>
    private static void CheckDeployedConfig(string configDirectory)
    {
        var repository = new DirectoryInfo(configDirectory).Parent;

        if (repository is null) return;

        var deployed = Path.Combine(repository.FullName, "publish", ConfigDirectory.Name);

        if (!Directory.Exists(deployed) || PathsMatch(deployed, configDirectory)) return;

        var stale = new List<string>();

        foreach (var (_, fileName) in SettingKeys.Seeds)
        {
            var there = Path.Combine(deployed, fileName);

            if (!File.Exists(there))
            {
                stale.Add(fileName);
                continue;
            }

            if (!SettingComparison.SameJson(
                    ConfigDirectory.Read(configDirectory, fileName),
                    File.ReadAllText(there)))
            {
                stale.Add(fileName);
            }
        }

        if (stale.Count == 0) return;

        Log.Warning(
            "publish/config is behind the repository in {Count} file(s): {Files}. The worker reads "
            + "that copy, so an emptied setting table would reseed from it. Copy config/ over it "
            + "(deploy/install-windows.ps1 does, `dotnet publish` on its own does not).",
            stale.Count,
            string.Join(", ", stale));
    }

    /// <summary>
    /// Warns when ui/swagger.json has fallen behind the api's own document.
    /// </summary>
    /// <remarks>
    /// Says nothing when the api is not answering. Preflight is run before a
    /// deploy and from machines with no interface checkout; a check that failed
    /// because something was switched off would be a check people learn to
    /// ignore. Compares endpoint names and the field names on each type, which
    /// is what the generator turns into methods and models - not descriptions,
    /// which change when a comment is reworded and mean nothing to the client.
    /// </remarks>
    private static async Task CheckGeneratedClientAsync(CancellationToken cancellationToken)
    {
        var generated = Path.Combine(RepositoryRoot(), "ui", "swagger.json");

        if (!File.Exists(generated)) return;

        var address = Environment.GetEnvironmentVariable("PR_API_URL") ?? "http://localhost:60080";

        JsonDocument live;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            var body = await http
                .GetStringAsync($"{address.TrimEnd('/')}/swagger/v1/swagger.json", cancellationToken)
                .ConfigureAwait(false);

            live = JsonDocument.Parse(body);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return;
        }

        using (live)
        {
            using var stored = JsonDocument.Parse(await File.ReadAllTextAsync(generated, cancellationToken).ConfigureAwait(false));

            var behind = new List<string>();

            behind.AddRange(Missing(live.RootElement, stored.RootElement, "paths")
                .Select(name => $"uc {name}"));

            foreach (var name in Missing(live.RootElement, stored.RootElement, "components", "schemas"))
            {
                behind.Add($"tip {name}");
            }

            // Field by field on the types both sides know about. This is the
            // half that bit: no endpoint appeared and no type appeared, one
            // request object simply grew a property.
            var liveSchemas = Section(live.RootElement, "components", "schemas");
            var storedSchemas = Section(stored.RootElement, "components", "schemas");

            if (liveSchemas is { } ls && storedSchemas is { } ss)
            {
                foreach (var schema in ls.EnumerateObject())
                {
                    if (!ss.TryGetProperty(schema.Name, out var theirs)) continue;

                    foreach (var field in Missing(schema.Value, theirs, "properties"))
                    {
                        behind.Add($"{schema.Name}.{field}");
                    }
                }
            }

            if (behind.Count == 0) return;

            Log.Warning(
                "ui/swagger.json is behind the running api in {Count} place(s): {Behind}. The Dart "
                + "client is generated from that file, so the interface cannot see these. "
                + "Regenerate: dart run swagger_parser && dart run build_runner build",
                behind.Count,
                string.Join(", ", behind.Take(12)));
        }
    }

    private static JsonElement? Section(JsonElement root, params string[] path)
    {
        var current = root;

        foreach (var step in path)
        {
            if (!current.TryGetProperty(step, out var next)) return null;

            current = next;
        }

        return current;
    }

    /// <summary>Names present in the first document's section and absent from the second's.</summary>
    private static IEnumerable<string> Missing(JsonElement live, JsonElement stored, params string[] path)
    {
        var here = Section(live, path);
        var there = Section(stored, path);

        if (here is not { } a) return [];
        if (there is not { } b) return a.EnumerateObject().Select(p => p.Name);

        return a.EnumerateObject()
            .Select(p => p.Name)
            .Where(name => !b.TryGetProperty(name, out _));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ProcurementRadar.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static bool PathsMatch(string left, string right)
        => string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

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
