using ProcurementRadar.Domain.Entities;

namespace ProcurementRadar.Domain.Repositories;

public interface IRawFetchRepository
{
    /// <summary>
    /// Takes the next id from the sequence without writing a row.
    /// </summary>
    /// <remarks>
    /// The archive path contains the id (D-04), so the id has to exist before the
    /// file can be written, and the file has to exist before the row is inserted.
    /// A leftover file with no row is recoverable by hand; a row pointing at a
    /// file that was never written is a hole in an archive that is never deleted.
    /// </remarks>
    Task<long> ReserveIdAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes one archive row. <see cref="RawFetch.Id"/> must be an id taken from
    /// <see cref="ReserveIdAsync"/>, and the body must already be on disk.
    /// </summary>
    Task InsertAsync(RawFetch rawFetch, CancellationToken cancellationToken);

    /// <summary>
    /// The most recent archive row for this source, or null if there is none.
    /// </summary>
    /// <remarks>
    /// Carries both things the next fetch needs: the hash, so an unchanged page
    /// does not produce a second archive row, and the ETag and Last-Modified to
    /// send back as conditional headers.
    /// </remarks>
    Task<RawFetch?> GetLatestAsync(long sourceId, CancellationToken cancellationToken);

    /// <summary>
    /// Rows still waiting for the parser, oldest first. Pass a source id to
    /// reprocess a single source after fixing its adapter.
    /// </summary>
    Task<IReadOnlyList<RawFetch>> ListUnprocessedAsync(
        long? sourceId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks a row as parsed, recording which parser version did it and how many
    /// notices came out.
    /// </summary>
    /// <remarks>
    /// The count is what the parser-broken alarm reads. An archive row exists
    /// only when the page changed, so a changed page that yielded nothing is
    /// exactly the condition in spec Bölüm 11 - and there is no other way to see
    /// it after the fact.
    /// </remarks>
    /// <summary>
    /// Marks every archived body of one source unprocessed, so the next parse
    /// run reads them all again.
    /// </summary>
    /// <remarks>
    /// Deliberately per-source and never for all of them: reprocessing 12,000
    /// rows because somebody forgot an argument is an hour of work and a lot of
    /// churn on notices that were fine.
    /// </remarks>
    /// <returns>How many rows were queued.</returns>
    Task<int> MarkSourceUnprocessedAsync(long sourceId, CancellationToken cancellationToken);

    Task MarkProcessedAsync(
        long rawFetchId,
        string parserVersion,
        int noticeCount,
        CancellationToken cancellationToken);
}
