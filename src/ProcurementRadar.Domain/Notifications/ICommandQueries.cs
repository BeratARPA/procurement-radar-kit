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

    /// <summary>Anything in the archive whose title or authority carries the term.</summary>
    /// <remarks>
    /// The archive is what this project is actually accumulating (spec Bolum 16),
    /// and until now the only way to ask it a question was psql.
    /// </remarks>
    Task<IReadOnlyList<CommandNotice>> SearchAsync(string term, int limit, CancellationToken cancellationToken);

    /// <summary>One notice in full, announcement text included.</summary>
    Task<NoticeDetail?> GetDetailAsync(string ikn, CancellationToken cancellationToken);

    /// <summary>Sources that are failing or have gone quiet.</summary>
    Task<IReadOnlyList<SourceHealth>> ListTroubledSourcesAsync(int silentDays, CancellationToken cancellationToken);

    /// <summary>What the team's own verdicts scored - the calibration data.</summary>
    Task<IReadOnlyList<ScoreByStatus>> GetScoreReportAsync(CancellationToken cancellationToken);

    /// <summary>Still new, closing within the window.</summary>
    Task<IReadOnlyList<CommandNotice>> ListClosingSoonAsync(
        int withinHours,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>One notice with everything worth reading on a phone.</summary>
public sealed record NoticeDetail
{
    public long NoticeId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string AuthorityName { get; init; } = string.Empty;

    public string? Ikn { get; init; }

    public int Score { get; init; }

    public string? Rationale { get; init; }

    public DateTimeOffset? BidDeadlineAt { get; init; }

    public string? AnnouncementText { get; init; }
}

public sealed record SourceHealth
{
    public string AuthorityName { get; init; } = string.Empty;

    public int ConsecutiveFailures { get; init; }

    public DateTimeOffset? LastSuccessAt { get; init; }

    public DateTimeOffset? LastNewNoticeAt { get; init; }
}

/// <summary>
/// How the team's verdicts line up with the score.
/// </summary>
/// <remarks>
/// The threshold is a guess until this says otherwise. spec Bolum 10 keeps the
/// button presses for exactly this: if what was dismissed and what was pursued
/// score the same, the filter is not discriminating and the number needs moving.
/// </remarks>
public sealed record ScoreByStatus
{
    public string Status { get; init; } = string.Empty;

    public int Count { get; init; }

    public double AverageScore { get; init; }

    public int MaximumScore { get; init; }
}
