using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProcurementRadar.Collector;

/// <summary>
/// The politeness budget as a setting, with the floors that cannot be crossed.
/// </summary>
/// <remarks>
/// D-16 opened these to the settings screen and kept the limits on the server.
/// The reasoning is CLAUDE.md's: there is no speed to be gained and there is an
/// IP to be lost, and a settings screen must not make that possible with one
/// mistyped number.
///
/// The floors live here rather than in the api, so they hold for whoever writes
/// the value - the screen, a curl, a hand-edited config file. A setting that
/// breaks one is refused at the api and, if it somehow reaches the database,
/// fails to load in the worker, which keeps the previous value and logs an error.
/// Two doors, one lock.
/// </remarks>
public sealed record PolitenessSettings
{
    /// <summary>CLAUDE.md: minimum five seconds between requests to one host.</summary>
    public const int MinimumHostIntervalFloorSeconds = 5;

    [JsonPropertyName("minimum_host_interval_seconds")]
    public int MinimumHostIntervalSeconds { get; init; } = MinimumHostIntervalFloorSeconds;

    [JsonPropertyName("maximum_jitter_seconds")]
    public int MaximumJitterSeconds { get; init; } = 3;

    [JsonPropertyName("respect_robots")]
    public bool RespectRobots { get; init; } = true;

    [JsonPropertyName("night_window_start_hour")]
    public int NightWindowStartHour { get; init; } = 2;

    [JsonPropertyName("night_window_end_hour")]
    public int NightWindowEndHour { get; init; } = 5;

    [JsonPropertyName("night_interval_multiplier")]
    public int NightIntervalMultiplier { get; init; } = 3;

    [JsonPropertyName("request_timeout_seconds")]
    public int RequestTimeoutSeconds { get; init; } = 30;

    [JsonPropertyName("maximum_attempts")]
    public int MaximumAttempts { get; init; } = 3;

    public static PolitenessSettings Load(string json)
    {
        var settings = JsonSerializer.Deserialize<PolitenessSettings>(json)
            ?? throw new InvalidOperationException("politeness.json is empty.");

        var refusals = settings.Refusals().ToList();

        if (refusals.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", refusals));
        }

        return settings;
    }

    /// <summary>Every reason this setting cannot be accepted, in one message.</summary>
    /// <remarks>
    /// All of them at once rather than the first: somebody correcting a form
    /// should not have to submit it four times to find out what else is wrong.
    /// </remarks>
    private IEnumerable<string> Refusals()
    {
        if (MinimumHostIntervalSeconds < MinimumHostIntervalFloorSeconds)
        {
            yield return $"minimum_host_interval_seconds cannot be below "
                + $"{MinimumHostIntervalFloorSeconds} (CLAUDE.md, HTTP politeness).";
        }

        if (!RespectRobots)
        {
            yield return "respect_robots cannot be false; robots.txt is obeyed (CLAUDE.md, spec section 12).";
        }

        if (MaximumJitterSeconds < 0)
        {
            yield return "maximum_jitter_seconds cannot be negative.";
        }

        if (NightIntervalMultiplier < 1)
        {
            // Below one would poll harder at night, which inverts the rule
            // instead of relaxing it.
            yield return "night_interval_multiplier cannot be below 1.";
        }

        if (NightWindowStartHour is < 0 or > 23 || NightWindowEndHour is < 0 or > 24)
        {
            yield return "The night window has to be hours of a day.";
        }

        if (MaximumAttempts is < 1 or > 5)
        {
            // The cap is politeness too: a source that is failing is a source
            // being asked the same question over and over.
            yield return "maximum_attempts has to be between 1 and 5.";
        }

        if (RequestTimeoutSeconds is < 5 or > 120)
        {
            yield return "request_timeout_seconds has to be between 5 and 120.";
        }
    }

    /// <summary>The fetcher's options, built from this setting.</summary>
    public HttpFetcherOptions ToFetcherOptions(string contactEmail) => new()
    {
        ContactEmail = contactEmail,
        MinimumHostInterval = TimeSpan.FromSeconds(MinimumHostIntervalSeconds),
        MaximumJitter = TimeSpan.FromSeconds(MaximumJitterSeconds),
        RequestTimeout = TimeSpan.FromSeconds(RequestTimeoutSeconds),
        MaximumAttempts = MaximumAttempts
    };
}
