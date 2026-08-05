using Microsoft.Extensions.Logging;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Adapters;
using ProcurementRadar.Domain.Normalization;
using ProcurementRadar.Parsers;
using ProcurementRadar.Persistence;
using ProcurementRadar.Scoring;
using Serilog;
using Serilog.Extensions.Logging;

namespace ProcurementRadar.Cli;

/// <summary>
/// Runs archived bodies back through the parsers.
/// </summary>
/// <remarks>
/// This is the command that makes the archive worth keeping. A parser that broke
/// when an authority redesigned its site is fixed, this is pointed at that
/// source, and the notices that were missed appear - with their original
/// first_seen_at, because the upsert never recomputes it.
/// </remarks>
internal static class ReparseCommand
{
    private const int DefaultBatchSize = 500;

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var sourceIdText = ReadOption(args, "--source-id");
        long? sourceId = null;

        if (sourceIdText is not null)
        {
            if (!long.TryParse(sourceIdText, out var parsed) || parsed <= 0)
            {
                Log.Error("--source-id must be a positive number, got '{Value}'", sourceIdText);
                return 1;
            }

            sourceId = parsed;
        }

        var batchSize = DefaultBatchSize;
        if (ReadOption(args, "--batch-size") is { } batchText
            && (!int.TryParse(batchText, out batchSize) || batchSize <= 0))
        {
            Log.Error("--batch-size must be a positive number, got '{Value}'", batchText);
            return 1;
        }

        var connectionString = CliConfiguration.ConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Log.Error(
                "PR_DB_CONNECTION_STRING is not set. See docs/decisions.md D-06.");
            return 1;
        }

        ConfigFiles config;
        try
        {
            config = ConfigFiles.LoadFrom(ReadOption(args, "--config-dir"));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or DirectoryNotFoundException)
        {
            Log.Error(exception, "Could not load the config files");
            return 1;
        }

        PersistenceSetup.RegisterTypeHandlers();

        using var loggerFactory = new SerilogLoggerFactory(Log.Logger);
        await using var connectionFactory = new NpgsqlConnectionFactory(connectionString);

        var paths = new StoragePathBuilder(
            Environment.GetEnvironmentVariable(StoragePathBuilder.DataDirectoryVariable)
            ?? StoragePathBuilder.DefaultDataDirectory);

        var normalizer = new NoticeNormalizer(config.Normalization);

        var pipeline = new ParsePipeline(
            new FileSystemRawArchive(paths, loggerFactory.CreateLogger<FileSystemRawArchive>()),
            new RawFetchRepository(connectionFactory),
            new SourceRepository(connectionFactory, TimeProvider.System),
            new NoticeRepository(connectionFactory, TimeProvider.System),
            new SourceAdapterRegistry(
            [
                new GenericTableAdapter(
                    new UnusableFetcher(),
                    new TurkishDateParser(
                        config.DatePatterns,
                        loggerFactory.CreateLogger<TurkishDateParser>()),
                    loggerFactory.CreateLogger<GenericTableAdapter>())
            ]),
            new ExternalKeyBuilder(normalizer),
            new PersonalDataMasker(config.PersonalData),
            new NoticeDocumentRepository(connectionFactory),
            new RuleScorer(config.Keywords, normalizer),
            new NoticeScoreRepository(connectionFactory, TimeProvider.System),
            loggerFactory.CreateLogger<ParsePipeline>());

        var summary = await pipeline.RunAsync(sourceId, batchSize, cancellationToken).ConfigureAwait(false);

        Log.Information(
            "Reparse finished for {Scope}: {Rows} archive row(s), {Seen} notice(s), {New} new, {Skipped} skipped",
            sourceId is null ? "every source" : $"source {sourceId}",
            summary.ArchiveRowsRead,
            summary.NoticesSeen,
            summary.NoticesNew,
            summary.RowsSkipped);

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

    /// <summary>
    /// An adapter still needs a fetcher to be constructed, but reparsing must
    /// never reach the network. This one says so out loud rather than quietly
    /// making a request.
    /// </summary>
    private sealed class UnusableFetcher : Domain.Fetching.IHttpFetcher
    {
        public Task<Domain.Fetching.FetchResult> FetchAsync(
            Domain.Fetching.FetchRequest request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("reparse reads the archive; it does not fetch.");
    }
}
