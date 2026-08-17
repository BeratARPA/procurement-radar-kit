using Dapper;
using Microsoft.Extensions.Logging;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Normalization;
using ProcurementRadar.Persistence;
using Serilog;
using Serilog.Extensions.Logging;

namespace ProcurementRadar.Cli;

/// <summary>
/// Reads every downloaded document off disk again, extracts its text and masks
/// it before writing it back.
/// </summary>
/// <remarks>
/// Written for a fault, and the fault was an omission: DocumentDownloader put
/// the extracted text of every şartname straight into the column with no mask on
/// it. spec.md Bölüm 12 and CLAUDE.md both say personal data must not reach the
/// database, and the announcement path had a masker the whole time - this one
/// never did. Measured before the fix: of 270 stored texts, 188 carried an email
/// address and 17 carried something shaped like a phone number.
///
/// Fixing the code only stops the next one. The rows already written are the
/// reason this command exists, and it is the archive that makes it possible: the
/// files were kept exactly as downloaded (D-11), so the text can be produced
/// again from them rather than being patched in place.
///
/// It also picks up the masking fix that goes with it - the value no longer eats
/// the leading digit of the following item number.
///
/// Rows with no storage_path are left alone. Those are EKAP announcements, which
/// were fetched and masked by EkapAnnouncementEnricher and have no file to read.
/// </remarks>
internal static class RemaskCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var dryRun = args.Contains("--dry-run", StringComparer.Ordinal);

        var connectionString = CliConfiguration.ConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Log.Error("PR_DB_CONNECTION_STRING is not set. See docs/decisions.md D-06.");
            return 1;
        }

        ConfigFiles config;
        try
        {
            config = ConfigFiles.LoadFrom(ReadOption(args, "--config-dir"));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            Log.Error(exception, "Could not read config");
            return 1;
        }

        var dataDirectory = Environment.GetEnvironmentVariable(StoragePathBuilder.DataDirectoryVariable)
            ?? StoragePathBuilder.DefaultDataDirectory;
        var paths = new StoragePathBuilder(dataDirectory);
        var masker = new PersonalDataMasker(config.PersonalData);

        using var loggerFactory = new SerilogLoggerFactory(Log.Logger);
        var extractor = new DocumentTextExtractor(loggerFactory.CreateLogger<DocumentTextExtractor>());

        var factory = new NpgsqlConnectionFactory(connectionString);
        await using var connection = await factory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var rows = (await connection.QueryAsync<StoredDocument>(
            new CommandDefinition(
                """
                SELECT id AS "Id", storage_path AS "StoragePath", file_name AS "FileName",
                       extracted_text AS "ExtractedText"
                FROM notice_document
                WHERE storage_path IS NOT NULL
                ORDER BY id
                """,
                cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();

        Log.Information("{Count} downloaded document(s) to read again", rows.Count);

        var changed = 0;
        var missing = 0;
        var unreadable = 0;

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var absolute = paths.Absolute(row.StoragePath);

            if (!File.Exists(absolute))
            {
                // Not fatal and worth counting. A row whose file is gone keeps
                // the text it has; deleting it would lose the only copy.
                missing++;
                continue;
            }

            string? text;
            try
            {
                var extracted = extractor.Extract(
                    await File.ReadAllBytesAsync(absolute, cancellationToken).ConfigureAwait(false),
                    row.FileName);

                text = masker.MaskText(extracted.Text);
            }
            catch (Exception exception)
            {
                unreadable++;
                Log.Warning(exception, "Could not read {Path} again", row.StoragePath);
                continue;
            }

            if (string.Equals(text, row.ExtractedText, StringComparison.Ordinal))
            {
                continue;
            }

            changed++;

            if (dryRun)
            {
                continue;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE notice_document SET extracted_text = @Text WHERE id = @Id",
                new { Text = text, row.Id },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        Log.Information(
            "{Verb}: {Changed} document(s) rewritten, {Missing} file(s) no longer on disk, {Unreadable} unreadable",
            dryRun ? "Would have" : "Done",
            changed,
            missing,
            unreadable);

        return 0;
    }

    private sealed record StoredDocument
    {
        public long Id { get; init; }

        public string StoragePath { get; init; } = string.Empty;

        public string? FileName { get; init; }

        public string? ExtractedText { get; init; }
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
