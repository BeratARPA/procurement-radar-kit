using ProcurementRadar.Domain.Enums;

namespace ProcurementRadar.Domain.Notifications;

public enum NotificationKind
{
    /// <summary>The first message about a notice.</summary>
    NewNotice,

    /// <summary>The one nudge as the deadline approaches.</summary>
    DeadlineReminder
}

/// <summary>
/// Everything one Telegram message needs, flattened out of the notice, its score
/// and its documents so the notifier does not have to go looking.
/// </summary>
public sealed record NoticeForNotification
{
    public required long NoticeId { get; init; }

    public required string Title { get; init; }

    public required string AuthorityName { get; init; }

    public string? Description { get; init; }

    public decimal? EstimatedCost { get; init; }

    public DateTimeOffset? BidDeadlineAt { get; init; }

    public required string SourceUrl { get; init; }

    public int? RuleScore { get; init; }

    /// <summary>Which keywords produced the score.</summary>
    public string? Rationale { get; init; }

    public int DocumentCount { get; init; }
}

public interface INotificationRepository
{
    /// <summary>
    /// Notices worth telling somebody about: scored at or above the threshold,
    /// still new, and never announced.
    /// </summary>
    Task<IReadOnlyList<NoticeForNotification>> ListUnannouncedAsync(
        int minimumScore,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Notices whose deadline is inside the window, still untouched by anybody,
    /// already announced once, and not yet reminded about.
    /// </summary>
    Task<IReadOnlyList<NoticeForNotification>> ListDueRemindersAsync(
        TimeSpan window,
        int minimumScore,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records that a message went out. Returns false if one of that kind had
    /// already been recorded, which is what stops a duplicate rather than any
    /// check the caller has to remember.
    /// </summary>
    Task<bool> TryRecordAsync(
        long noticeId,
        NotificationKind kind,
        NotificationChannel channel,
        string status,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gives the slot back when the message could not be sent after all.
    /// </summary>
    /// <remarks>
    /// The claim is taken before the send, so two things cannot both send. If
    /// the send then fails, keeping the claim would lose that notice for good,
    /// and a network blip is far more likely than a crash in the moment between
    /// a successful send and the row being marked. Releasing it means the next
    /// pass tries again.
    /// </remarks>
    Task ReleaseAsync(
        long noticeId,
        NotificationKind kind,
        NotificationChannel channel,
        CancellationToken cancellationToken);

    /// <summary>Marks a claimed slot as actually delivered.</summary>
    Task MarkSentAsync(
        long noticeId,
        NotificationKind kind,
        NotificationChannel channel,
        string status,
        CancellationToken cancellationToken);
}
