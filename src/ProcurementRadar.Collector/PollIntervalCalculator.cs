namespace ProcurementRadar.Collector;

/// <summary>
/// Decides how often a source may be polled and whether it is due.
/// </summary>
/// <remarks>
/// Between 02:00 and 05:00 Europe/Istanbul the interval is tripled. Those are the
/// hours when authorities run maintenance, and hammering a site that is already
/// struggling is the fastest way to get noticed for the wrong reason.
/// </remarks>
public sealed class PollIntervalCalculator
{
    internal const int NightStartHour = 2;
    internal const int NightEndHour = 5;
    internal const int NightMultiplier = 3;

    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private readonly TimeProvider _timeProvider;

    public PollIntervalCalculator(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public TimeSpan Effective(int pollIntervalMinutes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pollIntervalMinutes);

        return TimeSpan.FromMinutes(pollIntervalMinutes * (IsNightWindow() ? NightMultiplier : 1));
    }

    public bool IsDue(DateTimeOffset? lastFetchAt, int pollIntervalMinutes)
        => lastFetchAt is null
            || _timeProvider.GetUtcNow() - lastFetchAt.Value >= Effective(pollIntervalMinutes);

    internal bool IsNightWindow()
    {
        var local = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), Istanbul);

        return local.Hour is >= NightStartHour and < NightEndHour;
    }
}
