using ProcurementRadar.Domain.Entities;

namespace ProcurementRadar.Domain.Repositories;

public interface IRawFetchRepository
{
    /// <summary>Writes one archive row and returns its generated id.</summary>
    Task<long> InsertAsync(RawFetch rawFetch, CancellationToken cancellationToken);

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
