namespace ProcurementRadar.Collector;

/// <summary>
/// The politeness budget. The defaults are the hard requirements from CLAUDE.md,
/// not suggestions: breaking them gets the project's IP banned, and a
/// fifteen-minute poll interval is already far more than enough.
/// </summary>
public sealed record HttpFetcherOptions
{
    /// <summary>Used in the User-Agent so an authority can reach a human.</summary>
    public required string ContactEmail { get; init; }

    /// <summary>Shortest gap between two requests to the same host.</summary>
    public TimeSpan MinimumHostInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Random extra delay on top, so the traffic does not look metronomic.</summary>
    public TimeSpan MaximumJitter { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>How long a successfully read robots.txt is trusted.</summary>
    public TimeSpan RobotsCacheDuration { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How long a robots.txt we could not read is treated as a refusal. Short on
    /// purpose: a server hiccup should not silence a source for a day.
    /// </summary>
    public TimeSpan RobotsRetryDuration { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long a robots.txt is held closed when the failure was this machine's
    /// own network rather than the host.
    /// </summary>
    /// <remarks>
    /// Only long enough to answer the follow-up question asked about the same
    /// fetch, and gone well before the next poll. The fifteen minutes above is
    /// a back-off owed to a server that answered badly; a name that did not
    /// resolve owes nobody anything, and keeping that verdict cost 112 polls
    /// while other hosts were answering normally.
    /// </remarks>
    public TimeSpan RobotsOutageRetryDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Total tries, not retries. Three means one attempt plus two retries.</summary>
    public int MaximumAttempts { get; init; } = 3;

    /// <summary>First retry delay; doubles each time.</summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public string UserAgent => $"ProcurementRadar/1.0 (+mailto:{ContactEmail})";

    /// <summary>
    /// The token an authority would write in robots.txt to address this crawler.
    /// </summary>
    public string RobotsUserAgentToken => "ProcurementRadar";
}
