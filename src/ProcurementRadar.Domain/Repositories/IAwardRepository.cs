using ProcurementRadar.Domain.Entities;

namespace ProcurementRadar.Domain.Repositories;

/// <summary>A concluded notice waiting to be asked about.</summary>
public sealed record AwardCandidate
{
    public long NoticeId { get; init; }

    public string Ikn { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;
}

/// <summary>How a tender ended. Table <c>award</c>.</summary>
public interface IAwardRepository
{
    /// <summary>
    /// Notices whose deadline passed at least <paramref name="ripeDays"/> ago and
    /// whose result is still unknown.
    /// </summary>
    /// <remarks>
    /// Ripeness is not politeness, it is publication lag: the result announcement
    /// goes up after the contract is signed, which is weeks after the bids were
    /// opened. Asking the day after the deadline gets a tender announcement and
    /// burns a browser round trip.
    /// </remarks>
    Task<IReadOnlyList<AwardCandidate>> ListPendingAsync(
        int ripeDays,
        int maximumAttempts,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records what came back. A null <paramref name="award"/> means the
    /// announcement carried no result yet - the attempt still counts.
    /// </summary>
    Task RecordAsync(long noticeId, Award? award, CancellationToken cancellationToken);

    /// <summary>Most recent results, newest first.</summary>
    Task<IReadOnlyList<AwardSummary>> ListRecentAsync(int limit, CancellationToken cancellationToken);
}

/// <summary>One concluded tender, as it is read out in the group.</summary>
public sealed record AwardSummary
{
    public string Title { get; init; } = string.Empty;

    public string AuthorityName { get; init; } = string.Empty;

    public string? WinnerName { get; init; }

    public decimal? ContractAmount { get; init; }

    public decimal? EstimatedCost { get; init; }

    public int? BidCount { get; init; }

    public DateTimeOffset? AwardedAt { get; init; }
}
