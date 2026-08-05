namespace ProcurementRadar.Domain.Health;

public enum HealthAlertKind
{
    /// <summary>Three fetches in a row have failed.</summary>
    FetchFailures,

    /// <summary>Ten in a row, so the source has been taken out of the rotation.</summary>
    SourceDeactivated,

    /// <summary>A page changed and the parser found nothing in it, having found things before.</summary>
    ParserBroken,

    /// <summary>A source that used to produce notices has produced none for a month.</summary>
    SilentSource,

    /// <summary>Nothing at all arrived, anywhere, for a day.</summary>
    NothingCollected,

    /// <summary>Not a failure. Recorded the same way so it goes out once a week.</summary>
    WeeklySummary
}

public sealed record UnhealthySource(long SourceId, string AuthorityName, int ConsecutiveFailures);

public sealed record SilentSource(long SourceId, string AuthorityName, DateTimeOffset LastNewNoticeAt);

public sealed record BrokenParser(long SourceId, string AuthorityName, int EmptyParses);

/// <summary>The numbers in the weekly summary (spec Bölüm 11).</summary>
public sealed record WeeklySummary(
    int ActiveSources,
    int SourcesPolled,
    int NoticesSeen,
    int NoticesNotified,
    int BidsSubmitted,
    int NotRelevant,
    int SourcesInTrouble);

public interface IHealthRepository
{
    Task<IReadOnlyList<UnhealthySource>> ListFailingAsync(
        int minimumFailures,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sources that changed and then parsed to nothing, having produced notices
    /// before. The last part matters: a page that has never yielded anything is
    /// either an empty listing or a source that was never configured properly,
    /// and neither is a parser that broke.
    /// </summary>
    Task<IReadOnlyList<BrokenParser>> ListBrokenParsersAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SilentSource>> ListSilentAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken);

    /// <summary>How many notices were first seen since a moment.</summary>
    Task<int> CountNoticesSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);

    Task<WeeklySummary> SummariseAsync(DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>
    /// True when this alarm has not been raised for this source inside the
    /// cooldown, and records it. Repeating an alarm every minute is the fastest
    /// way to get the channel muted, which costs the alarms that matter.
    /// </summary>
    Task<bool> TryRaiseAsync(
        HealthAlertKind kind,
        long? sourceId,
        TimeSpan cooldown,
        string? detail,
        CancellationToken cancellationToken);
}

/// <summary>Where an alarm goes. Logging only, until Telegram is configured.</summary>
public interface IAlertChannel
{
    Task SendAsync(string message, CancellationToken cancellationToken);
}
