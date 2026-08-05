using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using ProcurementRadar.Domain.Adapters;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Normalization;
using ProcurementRadar.Domain.Repositories;

namespace ProcurementRadar.Collector;

public sealed record ParseRunSummary(
    int ArchiveRowsRead,
    int NoticesSeen,
    int NoticesNew,
    int RowsSkipped);

/// <summary>
/// Reads archived bodies back, runs them through the adapter that owns the
/// source, and writes what comes out to <c>procurement_notice</c>.
/// </summary>
/// <remarks>
/// This is the half of the system that can be run again. Nothing here touches
/// the network, so a parser fixed six months from now can be pointed at
/// everything ever archived.
/// </remarks>
public sealed class ParsePipeline
{
    /// <summary>
    /// Recorded in <c>raw_fetch.parser_version</c>. Bump it when parse output
    /// changes in a way that makes old rows worth redoing; it is what tells you
    /// afterwards which rows were produced by which generation of the code.
    /// </summary>
    public const string ParserVersion = "1";

    private readonly IRawArchive _archive;
    private readonly IRawFetchRepository _rawFetches;
    private readonly ISourceRepository _sources;
    private readonly INoticeRepository _notices;
    private readonly ISourceAdapterRegistry _adapters;
    private readonly ExternalKeyBuilder _keyBuilder;
    private readonly PersonalDataMasker _masker;
    private readonly ILogger<ParsePipeline> _logger;

    public ParsePipeline(
        IRawArchive archive,
        IRawFetchRepository rawFetches,
        ISourceRepository sources,
        INoticeRepository notices,
        ISourceAdapterRegistry adapters,
        ExternalKeyBuilder keyBuilder,
        PersonalDataMasker masker,
        ILogger<ParsePipeline> logger)
    {
        _archive = archive;
        _rawFetches = rawFetches;
        _sources = sources;
        _notices = notices;
        _adapters = adapters;
        _keyBuilder = keyBuilder;
        _masker = masker;
        _logger = logger;
    }

    public async Task<ParseRunSummary> RunAsync(
        long? sourceId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var pending = await _rawFetches
            .ListUnprocessedAsync(sourceId, batchSize, cancellationToken)
            .ConfigureAwait(false);

        var seen = 0;
        var created = 0;
        var skipped = 0;

        foreach (var rawFetch in pending)
        {
            var outcome = await ProcessAsync(rawFetch, cancellationToken).ConfigureAwait(false);

            if (outcome is null)
            {
                skipped++;
                continue;
            }

            seen += outcome.Value.Seen;
            created += outcome.Value.Created;
        }

        _logger.LogInformation(
            "Parse run: {Rows} archive row(s), {Seen} notice(s) seen, {New} new, {Skipped} row(s) skipped",
            pending.Count,
            seen,
            created,
            skipped);

        return new ParseRunSummary(pending.Count, seen, created, skipped);
    }

    private async Task<(int Seen, int Created)?> ProcessAsync(
        RawFetch rawFetch,
        CancellationToken cancellationToken)
    {
        var source = await _sources.GetByIdAsync(rawFetch.SourceId, cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            _logger.LogError(
                "Archive row {RawFetchId} belongs to source {SourceId}, which no longer exists",
                rawFetch.Id,
                rawFetch.SourceId);

            return null;
        }

        var adapter = _adapters.For(source.AdapterKey);
        if (adapter is null)
        {
            // Deliberately not marked processed. Once the adapter exists the row
            // is picked up on the next run, which is the whole point of keeping
            // the archive.
            _logger.LogError(
                "No adapter claims key '{AdapterKey}' for source {SourceId}; leaving row {RawFetchId} unprocessed",
                source.AdapterKey,
                source.Id,
                rawFetch.Id);

            return null;
        }

        byte[] body;
        try
        {
            body = await _archive.ReadAsync(rawFetch.BodyPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            _logger.LogError(
                exception,
                "Could not read the archived body at {BodyPath} for row {RawFetchId}",
                rawFetch.BodyPath,
                rawFetch.Id);

            return null;
        }

        // D-11: the archive holds bytes, and the encoding is worked out here,
        // every time. A page decoded wrongly today can be decoded correctly on
        // the next run once BodyDecoder learns better.
        var (text, charSet) = BodyDecoder.Decode(body, CharSetOf(rawFetch.ContentType));

        var notices = adapter.Parse(text, source);

        _logger.LogInformation(
            "Row {RawFetchId} ({Source}) parsed to {Count} notice(s) from {ByteCount} bytes as {CharSet}",
            rawFetch.Id,
            source.AuthorityName,
            notices.Count,
            body.Length,
            charSet);

        var created = 0;

        foreach (var raw in notices)
        {
            if (await StoreAsync(raw, source, rawFetch, cancellationToken).ConfigureAwait(false))
            {
                created++;
            }
        }

        await _rawFetches
            .MarkProcessedAsync(rawFetch.Id, $"{adapter.Key}@{ParserVersion}", cancellationToken)
            .ConfigureAwait(false);

        if (created > 0)
        {
            // Feeds the silent-source alarm in T-15: a source that has stopped
            // producing anything is the failure nobody notices.
            await _sources
                .RecordFetchSuccessAsync(source.Id, foundNewNotice: true, cancellationToken)
                .ConfigureAwait(false);
        }

        return (notices.Count, created);
    }

    private async Task<bool> StoreAsync(
        RawNotice raw,
        Source source,
        RawFetch rawFetch,
        CancellationToken cancellationToken)
    {
        // Before anything is written, and centrally, so no adapter can forget.
        var masked = _masker.Mask(raw);

        var externalKey = _keyBuilder.Build(
            masked.Ikn,
            masked.AuthorityName ?? source.AuthorityName,
            masked.Title,
            masked.BidDeadlineAt);

        if (masked.BidDeadlineAt is null)
        {
            _logger.LogWarning(
                "No bid deadline could be read for '{Title}' ({SourceUrl})",
                masked.Title,
                masked.SourceUrl);
        }

        var result = await _notices.UpsertAsync(
            new ProcurementNotice
            {
                SourceId = source.Id,
                RawFetchId = rawFetch.Id,
                ExternalKey = externalKey,
                Ikn = masked.Ikn,
                AuthorityName = masked.AuthorityName ?? source.AuthorityName,
                Title = masked.Title,
                Description = masked.Description,
                EstimatedCost = masked.EstimatedCost,
                BidDeadlineAt = masked.BidDeadlineAt,
                SourceUrl = masked.SourceUrl
            },
            cancellationToken).ConfigureAwait(false);

        if (result.WasInserted)
        {
            _logger.LogInformation(
                "New notice {NoticeId}: {Title} ({Authority})",
                result.NoticeId,
                masked.Title,
                masked.AuthorityName ?? source.AuthorityName);
        }

        return result.WasInserted;
    }

    /// <summary>
    /// Pulls the charset out of a stored Content-Type. It is the one encoding
    /// signal that is not inside the archived bytes.
    /// </summary>
    private static string? CharSetOf(string? contentType)
        => !string.IsNullOrWhiteSpace(contentType)
            && MediaTypeHeaderValue.TryParse(contentType, out var parsed)
                ? parsed.CharSet
                : null;
}
