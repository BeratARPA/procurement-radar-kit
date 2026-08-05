using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Enums;

namespace ProcurementRadar.Domain.Repositories;

/// <summary>
/// Outcome of an upsert. <see cref="WasInserted"/> is what tells the pipeline
/// whether this is a notice worth notifying about.
/// </summary>
public sealed record NoticeUpsertResult(long NoticeId, bool WasInserted);

public interface INoticeRepository
{
    /// <summary>
    /// Inserts the notice, or, when <c>external_key</c> already exists, moves
    /// <c>last_seen_at</c> forward and leaves everything else alone.
    /// <c>first_seen_at</c> is stamped once on insert and never recomputed:
    /// it is the product's whole competitive edge. Both timestamps come from the
    /// injected <see cref="TimeProvider"/>, so whatever the caller put in those
    /// two properties is ignored.
    /// </summary>
    Task<NoticeUpsertResult> UpsertAsync(ProcurementNotice notice, CancellationToken cancellationToken);

    /// <summary>Notices in a given state, nearest deadline first.</summary>
    Task<IReadOnlyList<ProcurementNotice>> ListByStatusAsync(
        NoticeStatus status,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves a notice to a new state. Driven by the Telegram buttons in T-14.
    /// </summary>
    Task UpdateStatusAsync(long noticeId, NoticeStatus status, CancellationToken cancellationToken);
}
