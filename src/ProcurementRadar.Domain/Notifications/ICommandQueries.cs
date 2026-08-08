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

    /// <summary>
    /// Who is buying.
    /// </summary>
    /// <remarks>
    /// A command reply can leave this out because the reader asked for a short
    /// list and is about to open one of them. A screen showing fifty rows cannot:
    /// two authorities buy the same thing under nearly the same title, and
    /// spec Bolum 10 puts the authority on the second line of every message for
    /// the same reason.
    /// </remarks>
    public string AuthorityName { get; init; } = string.Empty;

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

    /// <summary>
    /// The same notice, found by its own id.
    /// </summary>
    /// <remarks>
    /// The IKN is what a person types into Telegram, and for that it is the right
    /// key. It is the wrong key for a list: of the 924 notices collected so far,
    /// 814 have no IKN at all - only EKAP issues them, and the authorities' own
    /// pages are most of the archive. A screen that lists a notice and cannot then
    /// open it is not a screen, so the id every row already carries is the key
    /// used here.
    /// </remarks>
    Task<NoticeDetail?> GetDetailByIdAsync(long noticeId, CancellationToken cancellationToken);

    /// <summary>Sources that are failing or have gone quiet.</summary>
    Task<IReadOnlyList<SourceHealth>> ListTroubledSourcesAsync(int silentDays, CancellationToken cancellationToken);

    /// <summary>What the team's own verdicts scored - the calibration data.</summary>
    Task<IReadOnlyList<ScoreByStatus>> GetScoreReportAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Everything the archive knows, in the shape a filter is tried against.
    /// </summary>
    /// <remarks>
    /// The filter screen asks a question no amount of reasoning answers: with
    /// this word list and this threshold, how many of the notices we already
    /// have would have been announced, and which ones. Six hundred rows of title
    /// and description is a fraction of a megabyte and scoring them takes
    /// milliseconds, so the answer is exact rather than sampled.
    /// </remarks>
    Task<IReadOnlyList<PreviewNotice>> ListForPreviewAsync(int limit, CancellationToken cancellationToken);

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

    /// <summary>Where the notice was read from, so a reader can go and see it.</summary>
    public string SourceUrl { get; init; } = string.Empty;

    /// <summary>new, reviewed, bid_submitted, missed or not_relevant.</summary>
    /// <remarks>
    /// Three buttons that do not know which one is already pressed are three ways
    /// to record the same verdict twice.
    /// </remarks>
    public string Status { get; init; } = string.Empty;
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

/// <summary>One archived notice, as a filter sees it.</summary>
public sealed record PreviewNotice
{
    public long NoticeId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string AuthorityName { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string[]? OkasCodes { get; init; }
}
