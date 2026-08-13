namespace ProcurementRadar.Domain.Health;

public enum HealthAlertKind
{
    /// <summary>Three fetches in a row have failed.</summary>
    FetchFailures,

    /// <summary>Ten in a row, so the source has been taken out of the rotation.</summary>
    SourceDeactivated,

    /// <summary>
    /// A quarantined source answered again and was put back in the rotation.
    /// </summary>
    /// <remarks>
    /// The only alarm here that is good news, and it still has to be sent. A
    /// source coming back changes what the system is watching, and the person who
    /// switched it off is the one who needs to know the reason stopped being true.
    /// </remarks>
    SourceRecovered,

    /// <summary>A page changed and the parser found nothing in it, having found things before.</summary>
    ParserBroken,

    /// <summary>A source that used to produce notices has produced none for a month.</summary>
    SilentSource,

    /// <summary>
    /// Pages are being archived and nothing is turning them into notices.
    /// </summary>
    /// <remarks>
    /// spec section 11 has an alarm for a source that goes quiet and had none
    /// for a pipeline that stops. The difference showed: a missing parameter
    /// killed every parse run for two hours, the collector carried on filling
    /// the archive, and the only thing that said so was a line in a log file.
    ///
    /// Nothing was lost, because unprocessed rows wait. That is also what makes
    /// this measurable without a new column: if rows fetched long ago are still
    /// unprocessed, the half of the system that reads them has stopped.
    /// </remarks>
    PipelineStalled,

    /// <summary>
    /// A source that has been fetched successfully for a week and has never
    /// produced a notice.
    /// </summary>
    /// <remarks>
    /// The gap the other two leave open. ParserBroken needs the source to have
    /// found something before; SilentSource needs a month of history to have
    /// gone quiet. A source that never worked in the first place trips neither,
    /// and looks perfect from the outside: the fetch succeeds, the hash changes,
    /// consecutive_failures stays at zero.
    ///
    /// It matters because selectors get written against a page that happened to
    /// be empty that day. Eleven sources were switched back on in exactly that
    /// state.
    /// </remarks>
    NeverProduced,

    /// <summary>Nothing at all arrived, anywhere, for a day.</summary>
    NothingCollected,

    /// <summary>Not a failure. Recorded the same way so it goes out once a week.</summary>
    WeeklySummary
}

// Written with init properties rather than positionally, like every other
// record Dapper materialises. A positional record makes Dapper match a
// constructor by exact parameter type, and Npgsql hands a timestamptz back
// as DateTime - so a constructor taking DateTimeOffset is never found and
// the query throws at run time. Property setters convert.
public sealed record UnhealthySource
{
    public required long SourceId { get; init; }

    public required string AuthorityName { get; init; }

    public required int ConsecutiveFailures { get; init; }
}

public sealed record SilentSource
{
    public required long SourceId { get; init; }

    public required string AuthorityName { get; init; }

    public required DateTimeOffset LastNewNoticeAt { get; init; }
}

public sealed record BrokenParser
{
    public required long SourceId { get; init; }

    public required string AuthorityName { get; init; }

    public required int EmptyParses { get; init; }
}

/// <summary>The numbers in the weekly summary (spec Bölüm 11).</summary>
public sealed record WeeklySummary
{
    public int ActiveSources { get; init; }

    public int SourcesPolled { get; init; }

    public int NoticesSeen { get; init; }

    public int NoticesNotified { get; init; }

    public int BidsSubmitted { get; init; }

    public int NotRelevant { get; init; }

    public int SourcesInTrouble { get; init; }
}

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

    /// <summary>
    /// Archived pages older than the cutoff that nothing has parsed yet.
    /// </summary>
    Task<int> CountUnparsedSinceAsync(DateTimeOffset olderThan, CancellationToken cancellationToken);

    /// <summary>Active sources fetched since before the cutoff that have never produced a notice.</summary>
    Task<IReadOnlyList<NeverProducedSource>> ListNeverProducedAsync(
        DateTimeOffset fetchedSince,
        CancellationToken cancellationToken);

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

/// <summary>
/// A source that has been fetched for a while and has produced nothing, ever.
/// </summary>
/// <remarks>
/// Its own type rather than SilentSource, whose LastNewNoticeAt cannot be null -
/// and null is the entire condition here. Reusing it would have compiled, passed
/// the tests with a fake behind the interface, and thrown against Npgsql, which
/// is a mistake this project has now paid for four times.
/// </remarks>
public sealed record NeverProducedSource
{
    public long SourceId { get; init; }

    public string AuthorityName { get; init; } = string.Empty;
}
