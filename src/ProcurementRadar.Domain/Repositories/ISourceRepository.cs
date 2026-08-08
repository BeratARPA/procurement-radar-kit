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
    /// The same registration, saying which row it landed in.
    /// </summary>
    /// <remarks>
    /// The seed only needs to know whether it created something. A screen has to
    /// be able to open what it just saved, and for that it needs the id.
    /// </remarks>
    Task<SavedSource> SaveDefinitionAsync(Source source, CancellationToken cancellationToken);

    /// <summary>
    /// Edits the definition of a source that already exists, url included.
    /// </summary>
    /// <remarks>
    /// Separate from the upsert because the upsert matches on the url: changing
    /// one there would register a second source rather than move this one, which
    /// is what happens when an authority moves its listing page.
    ///
    /// Leaves <c>is_active</c> and every health column untouched. Editing a
    /// selector must not be able to put a source back in the rotation or reset
    /// the counter that took it out.
    /// </remarks>
    Task<bool> UpdateDefinitionAsync(long sourceId, Source source, CancellationToken cancellationToken);

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

/// <summary>Which row a definition landed in, and whether it was new.</summary>
public readonly record struct SavedSource(long Id, bool WasCreated);
