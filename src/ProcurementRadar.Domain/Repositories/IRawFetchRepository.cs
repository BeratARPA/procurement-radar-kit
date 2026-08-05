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
    /// The hash of the most recent body archived for this source, or null if
    /// there is none yet. Most pages do not change for days, and an unchanged
    /// page must not produce a second archive row.
    /// </summary>
    Task<string?> GetLatestContentHashAsync(long sourceId, CancellationToken cancellationToken);

    /// <summary>
    /// Rows still waiting for the parser, oldest first. Pass a source id to
    /// reprocess a single source after fixing its adapter.
    /// </summary>
    Task<IReadOnlyList<RawFetch>> ListUnprocessedAsync(
        long? sourceId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks a row as parsed and records which parser version did it, so a later
    /// reprocess can tell old output from new.
    /// </summary>
    Task MarkProcessedAsync(long rawFetchId, string parserVersion, CancellationToken cancellationToken);
}
