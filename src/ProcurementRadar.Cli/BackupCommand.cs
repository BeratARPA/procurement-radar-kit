using System.Diagnostics;
using System.Globalization;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Settings;
using ProcurementRadar.Persistence;
using Serilog;

namespace ProcurementRadar.Cli;

/// <summary>
/// Copies the database and the archive somewhere else (D-24).
/// </summary>
/// <remarks>
/// D-04 asked for a weekly `tar czf` to a second disk and left it to a person.
/// It was never once taken. On 13 August 2026 the repository finally went to
/// GitHub and the code stopped being the thing at risk - but the code is not
/// what this project is accumulating. `spec.md` Bölüm 16 is explicit that the
/// value is the archive, and none of it existed anywhere but one SSD.
///
/// A command rather than a worker loop, for one reason: it shells out to
/// `docker` because pg_dump lives in the container and not on this machine. The
/// panel already runs commands (D-18) and the deployment already schedules
/// tasks (D-10), so both a button and a nightly run come for free, and the
/// long-running worker keeps its distance from the container runtime.
/// </remarks>
internal static class BackupCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var connectionString = CliConfiguration.ConnectionString();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Log.Error("PR_DB_CONNECTION_STRING is not set");
            return 1;
        }

        PersistenceSetup.RegisterTypeHandlers();

        await using var connectionFactory = new NpgsqlConnectionFactory(connectionString);

        var stored = await new SettingRepository(connectionFactory, TimeProvider.System)
            .GetAsync(SettingKeys.Backup, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            Log.Error("Setting '{Key}' is not in the table. Run the worker once to seed it", SettingKeys.Backup);
            return 1;
        }

        var settings = BackupSettings.Load(stored.Value);

        if (!settings.IsConfigured)
        {
            Log.Warning(
                "No backup destination is set. Nothing was copied. Set 'destination' in the {Key} setting",
                SettingKeys.Backup);
            return 0;
        }

        // A missing destination is the USB disk not being plugged in, which is a
        // normal Tuesday and not a failure. Said out loud, and the run ends
        // without a non-zero code so a scheduled task does not start alarming
        // about something the owner already knows.
        if (!Directory.Exists(settings.Destination))
        {
            try
            {
                Directory.CreateDirectory(settings.Destination);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Warning(
                    "Backup destination {Destination} is not there and could not be created; nothing was copied",
                    settings.Destination);
                return 0;
            }
        }

        var dryRun = args.Contains("--dry-run", StringComparer.Ordinal);

        // Through TimeProvider rather than the ambient clock, and not as a
        // formality: D-08 bans the static properties outright and ClockUsageTests
        // greps for them - in comments too, which is how this line got written
        // twice. A command is a composition root, so naming the system clock
        // here is the allowed way to say it.
        var now = TimeProvider.System.GetUtcNow();

        if (settings.Database)
        {
            await DumpDatabaseAsync(settings, now, dryRun, cancellationToken).ConfigureAwait(false);
        }

        if (settings.Archive)
        {
            MirrorArchive(settings, now, dryRun);
        }

        return 0;
    }

    /// <summary>
    /// pg_dump through docker, because it is not installed on this machine.
    /// </summary>
    /// <remarks>
    /// Custom format (-Fc), which is compressed and restorable table by table.
    /// It was 1.1 MB on 13 August 2026 against 2,323 notices - the source list
    /// with its selectors, every notice, every score, and `first_seen_at`, which
    /// architecture rule 3 calls the product's whole value.
    /// </remarks>
    private static async Task DumpDatabaseAsync(
        BackupSettings settings,
        DateTimeOffset now,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var folder = Path.Combine(settings.Destination, "db");
        Directory.CreateDirectory(folder);

        var name = $"procurement-radar-{now:yyyyMMdd-HHmm}.dump";
        var path = Path.Combine(folder, name);

        if (dryRun)
        {
            Log.Information("Would write {Path}", path);
            return;
        }

        var start = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in new[]
                 {
                     "exec", "-i", "procurement-radar-db",
                     "pg_dump", "-U", "procurement_radar", "-d", "procurement_radar", "-Fc"
                 })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("docker could not be started.");

        // Straight from the pipe to the file: the dump never sits in memory, and
        // a database that grows past what this machine has spare still works.
        await using (var file = File.Create(path))
        {
            await process.StandardOutput.BaseStream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            // Deleted rather than left behind. A zero-byte file in a folder of
            // real dumps is the one thing worse than no dump: it looks like a
            // backup and restores nothing.
            File.Delete(path);

            Log.Error("pg_dump failed ({Code}): {Error}", process.ExitCode, error.Trim());
            return;
        }

        var written = new FileInfo(path).Length;

        Log.Information("Wrote {Path} ({Size:N1} MB)", path, written / 1024d / 1024d);

        Prune(folder, settings.KeepDumps);
    }

    /// <summary>Keeps the newest few dumps and deletes the rest.</summary>
    private static void Prune(string folder, int keep)
    {
        var dumps = new DirectoryInfo(folder)
            .GetFiles("procurement-radar-*.dump")
            .OrderByDescending(file => file.Name, StringComparer.Ordinal)
            .Skip(keep)
            .ToList();

        foreach (var old in dumps)
        {
            old.Delete();
        }

        if (dumps.Count > 0)
        {
            Log.Information("Removed {Count} dump(s) older than the newest {Keep}", dumps.Count, keep);
        }
    }

    /// <summary>
    /// Mirrors data/raw and data/doc, copying only what is not there yet.
    /// </summary>
    /// <remarks>
    /// A mirror and not a dated snapshot, because nothing in the archive ever
    /// changes: `raw_fetch` rows are never deleted (architecture rule 2) and the
    /// files under them are written once. So a mirror is already a full copy,
    /// four weekly snapshots would be four copies of the same 418 MB, and the
    /// second run costs seconds instead of minutes.
    /// </remarks>
    private static void MirrorArchive(BackupSettings settings, DateTimeOffset now, bool dryRun)
    {
        var source = Environment.GetEnvironmentVariable(StoragePathBuilder.DataDirectoryVariable)
            ?? StoragePathBuilder.DefaultDataDirectory;

        if (!Directory.Exists(source))
        {
            Log.Warning("No archive at {Source}; nothing to mirror", source);
            return;
        }

        var target = Path.Combine(settings.Destination, "data");
        var stamp = Path.Combine(settings.Destination, ".last-archive-mirror");

        if (!dryRun && File.Exists(stamp))
        {
            var last = File.GetLastWriteTimeUtc(stamp);
            var due = last.AddDays(settings.ArchiveEveryDays);

            if (now.UtcDateTime < due)
            {
                Log.Information(
                    "Archive mirrored {Days:N0} day(s) ago; next one due {Due:yyyy-MM-dd}",
                    (now.UtcDateTime - last).TotalDays,
                    due);
                return;
            }
        }

        var copied = 0L;
        var bytes = 0L;
        var skipped = 0L;

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);

            // pgdata lives under data/ too and is a running database's files.
            // Copying those while Postgres has them open produces something that
            // looks like a backup and will not start; the dump above is how the
            // database is backed up.
            if (relative.StartsWith("pgdata", StringComparison.OrdinalIgnoreCase)) continue;

            var destination = Path.Combine(target, relative);

            if (File.Exists(destination) && new FileInfo(destination).Length == new FileInfo(file).Length)
            {
                skipped++;
                continue;
            }

            if (dryRun)
            {
                copied++;
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);

            copied++;
            bytes += new FileInfo(destination).Length;
        }

        if (dryRun)
        {
            Log.Information("Would copy {Copied} file(s), {Skipped} already there", copied, skipped);
            return;
        }

        File.WriteAllText(
            stamp,
            now.ToString("O", CultureInfo.InvariantCulture));

        Log.Information(
            "Mirrored {Copied} file(s) ({Size:N0} MB) to {Target}; {Skipped} already there",
            copied,
            bytes / 1024d / 1024d,
            target,
            skipped);
    }
}
