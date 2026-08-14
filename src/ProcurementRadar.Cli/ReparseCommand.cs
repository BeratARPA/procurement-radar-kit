using ProcurementRadar.Domain.Settings;
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

        var dateParser = new TurkishDateParser(
            new Fixed<TurkishDatePatterns>(config.DatePatterns),
            loggerFactory.CreateLogger<TurkishDateParser>());

        var rawFetches = new RawFetchRepository(connectionFactory);

        // --again puts the source's archive back in the queue. Without it this
        // command only picks up rows that were never parsed, which is not what
        // "reparse" means and not what D-21 describes it as doing.
        if (args.Contains("--again", StringComparer.Ordinal))
        {
            if (sourceId is not { } target)
            {
                Log.Error("--again needs --source-id. Reprocessing every source at once is not offered.");
                return 1;
            }

            var queued = await rawFetches
                .MarkSourceUnprocessedAsync(target, cancellationToken)
                .ConfigureAwait(false);

            Log.Information("Queued {Count} archived body(ies) of source {SourceId} to be read again",
                queued, target);
        }

        var pipeline = new ParsePipeline(
            new FileSystemRawArchive(paths, loggerFactory.CreateLogger<FileSystemRawArchive>()),
            rawFetches,
            new SourceRepository(connectionFactory, TimeProvider.System),
            new NoticeRepository(connectionFactory, TimeProvider.System),
            // Every adapter, not only the table one.
            //
            // This registry held GenericTableAdapter alone, so an archive row
            // belonging to any other source was skipped - and the two it was
            // skipping were the EKAP ones, which spec.md Bölüm 13 names as the
            // most fragile component in the project and says to keep the tests
            // tightest around. Architecture rule 2 promises the archive can be
            // reprocessed when a parser is fixed; for the parser most likely to
            // need it, the promise could not be kept.
            //
            // Neither EKAP adapter needs a browser here. The search client is
            // only reached from FetchAsync, and Parse is a pure function over
            // the saved response (D-12) - the same split that lets their tests
            // run without Playwright.
            new SourceAdapterRegistry(
            [
                new GenericTableAdapter(
                    new UnusableFetcher(),
                    dateParser,
                    loggerFactory.CreateLogger<GenericTableAdapter>()),
                new JsonListAdapter(
                    new UnusableFetcher(),
                    dateParser,
                    loggerFactory.CreateLogger<JsonListAdapter>()),
                new EkapAdapter(
                    new UnusableSearchClient(),
                    dateParser,
                    new Fixed<ProcedureCodes>(config.ProcedureCodes),
                    loggerFactory.CreateLogger<EkapAdapter>()),
                new EkapDirectProcurementAdapter(
                    new UnusableSearchClient(),
                    dateParser,
                    new Fixed<ProcedureCodes>(config.ProcedureCodes),
                    loggerFactory.CreateLogger<EkapDirectProcurementAdapter>())
            ]),
            new ExternalKeyBuilder(normalizer),
            new Domain.Settings.Fixed<PersonalDataMasker>(new PersonalDataMasker(config.PersonalData)),
            new NoticeDocumentRepository(connectionFactory),
            new RuleScorer(new Domain.Settings.Fixed<Scoring.KeywordRules>(config.Keywords), normalizer),
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
    private sealed class UnusableSearchClient : Domain.Fetching.IEkapSearchClient
    {
        public Task<Domain.Fetching.FetchResult> SearchAsync(
            Domain.Fetching.EkapSearchRequest request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("reparse reads the archive; it does not drive a browser.");

        public Task<Domain.Fetching.FetchResult> FetchAnnouncementAsync(
            string ikn,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("reparse reads the archive; it does not drive a browser.");
    }

    private sealed class UnusableFetcher : Domain.Fetching.IHttpFetcher
    {
        public Task<Domain.Fetching.FetchResult> FetchAsync(
            Domain.Fetching.FetchRequest request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("reparse reads the archive; it does not fetch.");
    }
}
