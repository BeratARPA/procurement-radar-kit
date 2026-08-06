namespace ProcurementRadar.Domain.Notifications;

/// <summary>One notice as a command reply shows it.</summary>
public sealed record CommandNotice(long NoticeId, string Title, int Score, DateTimeOffset? BidDeadlineAt);

/// <summary>What /durum answers with.</summary>
public sealed record SystemStatus(
    int ActiveSources,
    int FailingSources,
    int Notices,
    int NoticesToday,
    int Announced,
    DateTimeOffset? LastFetchAt);

/// <summary>
/// The reads behind the Telegram commands.
/// </summary>
/// <remarks>
/// Kept apart from the notification queries because they answer a different
/// question. Those ask "what has not been announced yet"; these ask "what is
/// still open" and "is this thing running" - the questions somebody asks from a
/// phone, with no other interface to look at.
/// </remarks>
public interface ICommandQueries
{
    /// <summary>Scored, still new, deadline not yet passed. Soonest first.</summary>
    Task<IReadOnlyList<CommandNotice>> ListOpenAsync(
        int minimumScore,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Everything first seen since a moment, whatever it scored.</summary>
    Task<IReadOnlyList<CommandNotice>> ListSeenSinceAsync(
        DateTimeOffset since,
        int limit,
        CancellationToken cancellationToken);

    Task<SystemStatus> GetStatusAsync(CancellationToken cancellationToken);
}
