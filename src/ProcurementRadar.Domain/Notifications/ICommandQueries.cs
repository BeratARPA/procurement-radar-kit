namespace ProcurementRadar.Domain.Notifications;

/// <summary>One notice as a command reply shows it.</summary>
/// <remarks>
/// Init properties, not a positional record, and the project has paid for this
/// lesson once already. Dapper matches a constructor by exact parameter type: a
/// timestamptz arrives as DateTime, so a constructor taking DateTimeOffset? is
/// not considered, and the whole read throws at run time with nothing failing at
/// compile time. Properties are set one at a time and convert cleanly.
/// </remarks>
public sealed record CommandNotice
{
    public long NoticeId { get; init; }

    public string Title { get; init; } = string.Empty;

    public int Score { get; init; }

    public DateTimeOffset? BidDeadlineAt { get; init; }
}

/// <summary>What /durum answers with.</summary>
public sealed record SystemStatus
{
    public int ActiveSources { get; init; }

    public int FailingSources { get; init; }

    public int Notices { get; init; }

    public int NoticesToday { get; init; }

    public int Announced { get; init; }

    public DateTimeOffset? LastFetchAt { get; init; }
}

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
