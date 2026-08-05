using Microsoft.Extensions.Logging;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Fetching;
using ProcurementRadar.Domain.Repositories;

namespace ProcurementRadar.Collector;

public enum ArchiveOutcome
{
    /// <summary>A new body was archived and a <c>raw_fetch</c> row written.</summary>
    Stored,

    /// <summary>The page is byte for byte what we already have. No new row.</summary>
    Unchanged,

    /// <summary>The server answered 304. No new row.</summary>
    NotModified,

    /// <summary>robots.txt forbids the page. Nothing happened.</summary>
    Skipped,

    /// <summary>Every attempt failed. The failure counter moved.</summary>
    Failed
}

public sealed record ArchiveRecord(ArchiveOutcome Outcome, long? RawFetchId = null, string? BodyPath = null);

/// <summary>
/// Turns the result of a fetch into an archive entry and the source's health
/// columns.
/// </summary>
public sealed class RawFetchRecorder
{
    private readonly IRawArchive _archive;
    private readonly IRawFetchRepository _rawFetches;
    private readonly ISourceRepository _sources;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RawFetchRecorder> _logger;

    public RawFetchRecorder(
        IRawArchive archive,
        IRawFetchRepository rawFetches,
        ISourceRepository sources,
        TimeProvider timeProvider,
        ILogger<RawFetchRecorder> logger)
    {
        _archive = archive;
        _rawFetches = rawFetches;
        _sources = sources;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ArchiveRecord> RecordAsync(
        Source source,
        FetchResult result,
        CancellationToken cancellationToken)
    {
        switch (result.Outcome)
        {
            case FetchOutcome.SkippedByRobots:
                // Neither a success nor a failure. Touching the health columns
                // would either reset a real failure streak or invent one.
                return new ArchiveRecord(ArchiveOutcome.Skipped);

            case FetchOutcome.Failed:
                await _sources.RecordFetchFailureAsync(source.Id, cancellationToken).ConfigureAwait(false);
                return new ArchiveRecord(ArchiveOutcome.Failed);

            case FetchOutcome.NotModified:
                // The conditional request did its job: we already know this body.
                await _sources.RecordFetchSuccessAsync(source.Id, foundNewNotice: false, cancellationToken)
                    .ConfigureAwait(false);
                return new ArchiveRecord(ArchiveOutcome.NotModified);
        }

        if (result.Body is null || result.ContentHash is null)
        {
            _logger.LogError(
                "Source {SourceId} reported a fetched page with no body; not archiving anything",
                source.Id);

            return new ArchiveRecord(ArchiveOutcome.Failed);
        }

        var previous = await _rawFetches
            .GetLatestAsync(source.Id, cancellationToken)
            .ConfigureAwait(false);

        if (string.Equals(previous?.ContentHash, result.ContentHash, StringComparison.Ordinal))
        {
            // Most pages sit unchanged for days. Archiving a byte-identical copy
            // every fifteen minutes would bury the ones that did change.
            _logger.LogInformation("Source {SourceId} is unchanged; no new archive row", source.Id);

            await _sources.RecordFetchSuccessAsync(source.Id, foundNewNotice: false, cancellationToken)
                .ConfigureAwait(false);

            return new ArchiveRecord(ArchiveOutcome.Unchanged);
        }

        var fetchedAt = _timeProvider.GetUtcNow();
        var rawFetchId = await _rawFetches.ReserveIdAsync(cancellationToken).ConfigureAwait(false);

        // File first, row second. The row must never name a file that is not
        // there; a file with no row can be found again by hand.
        var bodyPath = await _archive
            .WriteAsync(source.Id, rawFetchId, fetchedAt, result.Body, cancellationToken)
            .ConfigureAwait(false);

        await _rawFetches.InsertAsync(
            new RawFetch
            {
                Id = rawFetchId,
                SourceId = source.Id,
                FetchedAt = fetchedAt,
                HttpStatus = result.HttpStatus,
                ETag = result.ETag,
                LastModified = result.LastModified,
                ContentType = result.ContentType,
                ContentHash = result.ContentHash,
                BodyPath = bodyPath,
                IsProcessed = false
            },
            cancellationToken).ConfigureAwait(false);

        await _sources.RecordFetchSuccessAsync(source.Id, foundNewNotice: false, cancellationToken)
            .ConfigureAwait(false);

        return new ArchiveRecord(ArchiveOutcome.Stored, rawFetchId, bodyPath);
    }
}
