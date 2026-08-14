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

    /// <summary>
    /// Writes the procurement type and procedure code onto a notice that has
    /// neither, and leaves any value already there untouched.
    /// </summary>
    /// <remarks>
    /// The one way a stored notice changes after its first insert, and it is
    /// deliberately fill-only. <see cref="UpsertAsync"/> cannot do this: on a
    /// conflict it moves <c>last_seen_at</c> and nothing else, which is what
    /// keeps <c>first_seen_at</c> safe. This exists so that reprocessing the
    /// archive can reach notices collected before either field was read - the
    /// whole reason the archive is kept.
    /// </remarks>
    /// <returns>True when a row was actually filled.</returns>
    Task<bool> FillProcedureAsync(
        long noticeId,
        ProcurementType? procurementType,
        string? procedureCode,
        CancellationToken cancellationToken);

    /// <summary>Notices in a given state, nearest deadline first.</summary>
    Task<IReadOnlyList<ProcurementNotice>> ListByStatusAsync(
        NoticeStatus status,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves a notice to a new state. Driven by the Telegram buttons in T-14.
    /// </summary>
    Task UpdateStatusAsync(long noticeId, NoticeStatus status, CancellationToken cancellationToken);
}
