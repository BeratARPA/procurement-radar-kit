using ProcurementRadar.Domain.Entities;

namespace ProcurementRadar.Domain.Repositories;

public interface ISourceRepository
{
    /// <summary>Every source with <c>is_active = true</c>, oldest fetch first.</summary>
    Task<IReadOnlyList<Source>> ListActiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Every source, the switched-off ones included.
    /// </summary>
    /// <remarks>
    /// The collector only ever wants the active ones. A person wants all of
    /// them: twenty-five of these are registered and inactive, each with its
    /// reason in its notes, and one was switched off automatically after ten
    /// consecutive failures without anybody being able to see it afterwards.
    /// </remarks>
    Task<IReadOnlyList<Source>> ListAllAsync(CancellationToken cancellationToken);

    /// <summary>Puts a source back in the rotation and clears its failure count.</summary>
    Task ActivateAsync(long sourceId, CancellationToken cancellationToken);

    /// <summary>
    /// One source, active or not. The parse pipeline needs it to know which
    /// adapter an archived body belongs to, and reprocessing has to work for a
    /// source that was taken out of the rotation.
    /// </summary>
    Task<Source?> GetByIdAsync(long sourceId, CancellationToken cancellationToken);

    /// <summary>
    /// Registers a source, or refreshes the definition of one already known
    /// under the same url. Returns true when the row was new.
    /// </summary>
    /// <remarks>
    /// Only the definition is overwritten. The health columns are left exactly
    /// as they are, so re-running the seed cannot reset a failure counter or
    /// wipe <c>last_new_notice_at</c>, which the silent-source alarm reads.
    /// </remarks>
    Task<bool> UpsertAsync(Source source, CancellationToken cancellationToken);

    /// <summary>
    /// Records a fetch that reached the page. Clears the failure counter and
    /// moves <c>last_new_notice_at</c> only when the fetch actually produced a
    /// notice we had not seen before, because the silent-source alarm in T-15
    /// reads that column.
    /// </summary>
    Task RecordFetchSuccessAsync(long sourceId, bool foundNewNotice, CancellationToken cancellationToken);

    /// <summary>Records a fetch that failed and increments the failure counter.</summary>
    Task RecordFetchFailureAsync(long sourceId, CancellationToken cancellationToken);

    /// <summary>Stamps <c>robots_checked_at</c> after a robots.txt lookup.</summary>
    Task RecordRobotsCheckAsync(long sourceId, CancellationToken cancellationToken);

    /// <summary>
    /// Takes a source out of the rotation. T-15 calls this once
    /// <c>consecutive_failures</c> reaches ten, so that a dead page stops
    /// burning politeness budget until somebody looks at it.
    /// </summary>
    Task DeactivateAsync(long sourceId, CancellationToken cancellationToken);
}
