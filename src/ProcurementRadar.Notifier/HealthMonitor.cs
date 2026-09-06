using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Health;
using ProcurementRadar.Domain.Repositories;

namespace ProcurementRadar.Notifier;

public sealed record HealthOptions
{
    /// <summary>
    /// How long an archived page may wait to be parsed before that is a fault.
    /// </summary>
    /// <remarks>
    /// The parser runs every minute and takes twenty-five rows a time, so half
    /// an hour is several hundred chances. Anything still waiting after that is
    /// not a queue, it is a stop.
    /// </remarks>
    public TimeSpan PipelineStallWindow { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long a source may be fetched without producing anything before that
    /// is a fault rather than a quiet week.
    /// </summary>
    /// <remarks>
    /// Seven days is roughly seven hundred polls. A listing that is genuinely
    /// empty for a week is possible; selectors that never matched anything look
    /// exactly the same, and only one of the two is worth an evening.
    /// </remarks>
    public TimeSpan NeverProducedWindow { get; init; } = TimeSpan.FromDays(7);

    /// <summary>How long this one alarm stays quiet after it has been raised.</summary>
    /// <remarks>
    /// A week rather than the usual day. A source that has produced nothing is
    /// still producing nothing tomorrow; saying so every morning trains the
    /// reader to ignore the channel, which is the failure spec Bölüm 9 describes
    /// for notifications and applies just as well to alarms.
    /// </remarks>
    public TimeSpan NeverProducedCooldown { get; init; } = TimeSpan.FromDays(7);

    /// <summary>Spec Bölüm 11: three failures in a row is worth saying out loud.</summary>
    public int FailureWarningThreshold { get; init; } = 3;

    /// <summary>Ten, and the source comes out of the rotation.</summary>
    public int FailureDeactivationThreshold { get; init; } = 10;

    /// <summary>A source that used to produce notices and has produced none for this long.</summary>
    public TimeSpan SilenceWindow { get; init; } = TimeSpan.FromDays(30);

    /// <summary>Nothing anywhere for this long is a system-wide problem.</summary>
    /// <remarks>
    /// Forty-eight hours, and the number comes from the archive rather than from
    /// a guess. The longest stretch this system has ever gone without a new
    /// notice while working perfectly is 39 hours - from Saturday 22 August at
    /// 09:11 - and the runners-up are 22.7 and 21.6. A 24-hour window sits
    /// underneath that, which is why it cried wolf on Sunday 23 August.
    ///
    /// This deliberately knows nothing about which day it is. An earlier version
    /// stepped over Saturdays and Sundays, on the theory that authorities do not
    /// publish then; measured over the whole archive they publish 79 notices on
    /// an average Saturday and 22 on a Sunday against 283-425 on a weekday.
    /// Quieter, not silent - so a weekend that produces nothing is worth hearing
    /// about, and a window that skipped weekends could not say so until Monday.
    /// </remarks>
    public TimeSpan CollectionWindow { get; init; } = TimeSpan.FromHours(48);

    /// <summary>How far back to look for empty parses.</summary>
    public TimeSpan ParserWindow { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How recently the collector must have archived something for it to count
    /// as alive.
    /// </summary>
    /// <remarks>
    /// Two hours against a fifteen minute poll interval, tripled to
    /// forty-five between 02:00 and 05:00. Over a measured month the only
    /// stretches of two hours with nothing archived at all were the day the
    /// worker was dead and an afternoon the machine was asleep.
    /// </remarks>
    public TimeSpan CollectorPulse { get; init; } = TimeSpan.FromHours(2);

    /// <summary>
    /// How long the same alarm stays quiet after being raised. An alarm that
    /// repeats every minute gets the channel muted, and a muted channel costs
    /// the alarms that matter.
    /// </summary>
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromHours(24);
}

public sealed record HealthRunSummary(int AlertsRaised);

/// <summary>
/// Watches for the failures nobody would otherwise notice (spec Bölüm 11).
/// </summary>
/// <remarks>
/// The dangerous failure is not the system crashing. It is HTTP 200, a parser
/// returning nothing, no alarm anywhere, and three months of seeing no notices
/// at all.
/// </remarks>
public sealed class HealthMonitor
{
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private readonly IHealthRepository _health;
    private readonly ISourceRepository _sources;
    private readonly IAlertChannel _alerts;
    private readonly ProcurementRadar.Domain.Settings.ICurrent<MessageTemplates> _templates;
    private readonly HealthOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HealthMonitor> _logger;

    public HealthMonitor(
        IHealthRepository health,
        ISourceRepository sources,
        IAlertChannel alerts,
        ProcurementRadar.Domain.Settings.ICurrent<MessageTemplates> templates,
        HealthOptions options,
        TimeProvider timeProvider,
        ILogger<HealthMonitor> logger)
    {
        _health = health;
        _sources = sources;
        _alerts = alerts;
        _templates = templates;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<HealthRunSummary> RunAsync(CancellationToken cancellationToken)
    {
        var raised = 0;

        raised += await CheckFailuresAsync(cancellationToken).ConfigureAwait(false);
        raised += await CheckParsersAsync(cancellationToken).ConfigureAwait(false);
        raised += await CheckSilentSourcesAsync(cancellationToken).ConfigureAwait(false);
        raised += await CheckCollectionAsync(cancellationToken).ConfigureAwait(false);
        raised += await CheckPipelineAsync(cancellationToken).ConfigureAwait(false);
        raised += await CheckNeverProducedAsync(cancellationToken).ConfigureAwait(false);

        return new HealthRunSummary(raised);
    }

    private async Task<int> CheckFailuresAsync(CancellationToken cancellationToken)
    {
        var failing = await _health
            .ListFailingAsync(_options.FailureWarningThreshold, cancellationToken)
            .ConfigureAwait(false);

        var raised = 0;

        foreach (var source in failing)
        {
            if (source.ConsecutiveFailures >= _options.FailureDeactivationThreshold)
            {
                // Taken out of the rotation before the alarm, so a failure to
                // send the message does not leave it polling a dead page.
                //
                // HealthRule rather than Manual: this is the one reason nobody
                // chose, and the only one the quarantine sweep will revisit
                // without being asked.
                await _sources
                    .DeactivateAsync(source.SourceId, DeactivationReason.HealthRule, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogError(
                    "Source {SourceId} ({Authority}) deactivated after {Count} consecutive failures",
                    source.SourceId,
                    source.AuthorityName,
                    source.ConsecutiveFailures);

                raised += await RaiseAsync(
                    HealthAlertKind.SourceDeactivated,
                    source.SourceId,
                    Fill(_templates.Value.Alerts.SourceDeactivated, new()
                    {
                        ["authority"] = source.AuthorityName,
                        ["count"] = source.ConsecutiveFailures.ToString(CultureInfo.InvariantCulture)
                    }),
                    cancellationToken).ConfigureAwait(false);

                continue;
            }

            raised += await RaiseAsync(
                HealthAlertKind.FetchFailures,
                source.SourceId,
                Fill(_templates.Value.Alerts.FetchFailures, new()
                {
                    ["authority"] = source.AuthorityName,
                    ["count"] = source.ConsecutiveFailures.ToString(CultureInfo.InvariantCulture)
                }),
                cancellationToken).ConfigureAwait(false);
        }

        return raised;
    }

    private async Task<int> CheckParsersAsync(CancellationToken cancellationToken)
    {
        var broken = await _health
            .ListBrokenParsersAsync(_timeProvider.GetUtcNow() - _options.ParserWindow, cancellationToken)
            .ConfigureAwait(false);

        var raised = 0;

        foreach (var source in broken)
        {
            raised += await RaiseAsync(
                HealthAlertKind.ParserBroken,
                source.SourceId,
                Fill(_templates.Value.Alerts.ParserBroken, new()
                {
                    ["authority"] = source.AuthorityName,
                    ["count"] = source.EmptyParses.ToString(CultureInfo.InvariantCulture)
                }),
                cancellationToken).ConfigureAwait(false);
        }

        return raised;
    }

    private async Task<int> CheckSilentSourcesAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        var silent = await _health
            .ListSilentAsync(now - _options.SilenceWindow, cancellationToken)
            .ConfigureAwait(false);

        var raised = 0;

        foreach (var source in silent)
        {
            raised += await RaiseAsync(
                HealthAlertKind.SilentSource,
                source.SourceId,
                Fill(_templates.Value.Alerts.SilentSource, new()
                {
                    ["authority"] = source.AuthorityName,
                    ["days"] = ((int)(now - source.LastNewNoticeAt).TotalDays)
                        .ToString(CultureInfo.InvariantCulture)
                }),
                cancellationToken).ConfigureAwait(false);
        }

        return raised;
    }

    /// <summary>
    /// Pages arriving and nothing reading them.
    /// </summary>
    /// <remarks>
    /// The counterpart to the collection alarm, and the one that was missing.
    /// A stopped parser looks like a healthy system from every angle except this
    /// one: fetches succeed, the archive grows, sources stay green, and no
    /// notice is ever created. The backlog is the only thing that says so.
    ///
    /// Measured against rows old enough that the parser has had many turns at
    /// them, so a restart or a slow batch does not raise it.
    /// </remarks>
    /// <summary>
    /// Sources that have never produced anything.
    /// </summary>
    /// <remarks>
    /// The gap the other two leave. ParserBroken wants the source to have found
    /// something before; SilentSource wants a month of history to have stopped.
    /// A source whose selectors were written against a page that happened to be
    /// empty trips neither and looks healthy from every angle: the fetch
    /// succeeds, the hash moves, the failure count stays at zero.
    /// </remarks>
    private async Task<int> CheckNeverProducedAsync(CancellationToken cancellationToken)
    {
        var sources = await _health
            .ListNeverProducedAsync(
                _timeProvider.GetUtcNow() - _options.NeverProducedWindow,
                cancellationToken)
            .ConfigureAwait(false);

        var raised = 0;

        foreach (var source in sources)
        {
            raised += await RaiseAsync(
                HealthAlertKind.NeverProduced,
                source.SourceId,
                Fill(_templates.Value.Alerts.NeverProduced, new()
                {
                    ["authority"] = source.AuthorityName
                }),
                cancellationToken).ConfigureAwait(false);
        }

        return raised;
    }

    private async Task<int> CheckPipelineAsync(CancellationToken cancellationToken)
    {
        var olderThan = _timeProvider.GetUtcNow() - _options.PipelineStallWindow;
        var waiting = await _health
            .CountUnparsedSinceAsync(olderThan, cancellationToken)
            .ConfigureAwait(false);

        if (waiting == 0)
        {
            return 0;
        }

        return await RaiseAsync(
            HealthAlertKind.PipelineStalled,
            sourceId: null,
            Fill(_templates.Value.Alerts.PipelineStalled, new()
            {
                ["count"] = waiting.ToString(CultureInfo.InvariantCulture),
                ["minutes"] = ((int)_options.PipelineStallWindow.TotalMinutes)
                    .ToString(CultureInfo.InvariantCulture)
            }),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The catch-all in spec.md Bölüm 11: the system quietly returning nothing.
    /// </summary>
    /// <remarks>
    /// It asks two questions, because one was not enough. "Nothing new in the
    /// window" on its own is also what a quiet weekend looks like: the
    /// authorities publish nothing on a Saturday and there is nothing to find.
    /// The window was widened to 48 hours for exactly that reason and it was
    /// still not enough - the longest silence in the archive is 48.8 hours, and
    /// the weekend this was written on was heading past 60. A threshold set
    /// below the quiet it is meant to tolerate does not tolerate it.
    ///
    /// So the second question is whether the collector is working at all, and
    /// it separates the two states cleanly. While that 48 hour silence ran, the
    /// parser was still reading twenty thousand notices a day and every one of
    /// them was already known; during the day the worker was dead, not one page
    /// was archived for twenty-three hours.
    ///
    /// What this gives up: every source fetching normally while every parser
    /// returns nothing would no longer raise this alarm. That case has its own,
    /// per source, and it is the more informative of the two because it names
    /// the source.
    /// </remarks>
    private async Task<int> CheckCollectionAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var since = now - _options.CollectionWindow;

        if (await _health.CountNoticesSinceAsync(since, cancellationToken).ConfigureAwait(false) > 0)
        {
            return 0;
        }

        var pulse = await _health
            .CountFetchesSinceAsync(now - _options.CollectorPulse, cancellationToken)
            .ConfigureAwait(false);

        if (pulse > 0)
        {
            // Reading everything and finding nothing new. That is a quiet week,
            // not a broken one.
            _logger.LogInformation(
                "No new notice in {Hours}h, but {Fetches} page(s) archived in the last {Pulse}h: nothing new to find rather than nothing working",
                (int)_options.CollectionWindow.TotalHours,
                pulse,
                (int)_options.CollectorPulse.TotalHours);

            return 0;
        }

        return await RaiseAsync(
            HealthAlertKind.NothingCollected,
            sourceId: null,
            Fill(_templates.Value.Alerts.NothingCollected, new()
            {
                ["hours"] = ((int)_options.CollectionWindow.TotalHours).ToString(CultureInfo.InvariantCulture)
            }),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends the weekly figures if it is time and they have not gone out this
    /// week. Monday mid-morning in Istanbul: a report that lands at three on a
    /// Saturday morning is a report nobody reads.
    /// </summary>
    public async Task<bool> TrySendWeeklySummaryAsync(CancellationToken cancellationToken)
    {
        var local = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), Istanbul);

        if (local.DayOfWeek != DayOfWeek.Monday || local.Hour is < 9 or >= 11)
        {
            return false;
        }

        // The same one-per-window record the alarms use, with a week rather than
        // a day, so a restart inside the window does not send it twice.
        if (!await _health
            .TryRaiseAsync(
                HealthAlertKind.WeeklySummary,
                sourceId: null,
                TimeSpan.FromDays(6),
                detail: null,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return false;
        }

        await SendWeeklySummaryAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>The weekly figures from spec Bölüm 11.</summary>
    public async Task SendWeeklySummaryAsync(CancellationToken cancellationToken)
    {
        var summary = await _health
            .SummariseAsync(_timeProvider.GetUtcNow() - TimeSpan.FromDays(7), cancellationToken)
            .ConfigureAwait(false);

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["active_sources"] = summary.ActiveSources.ToString(CultureInfo.InvariantCulture),
            ["sources_polled"] = summary.SourcesPolled.ToString(CultureInfo.InvariantCulture),
            ["notices_seen"] = summary.NoticesSeen.ToString(CultureInfo.InvariantCulture),
            ["notices_notified"] = summary.NoticesNotified.ToString(CultureInfo.InvariantCulture),
            ["bids_submitted"] = summary.BidsSubmitted.ToString(CultureInfo.InvariantCulture),
            ["not_relevant"] = summary.NotRelevant.ToString(CultureInfo.InvariantCulture),
            ["sources_in_trouble"] = summary.SourcesInTrouble.ToString(CultureInfo.InvariantCulture),
            ["awaiting_decision"] = summary.AwaitingDecision.ToString(CultureInfo.InvariantCulture),
            ["oldest_awaiting_days"] = summary.OldestAwaitingDays.ToString(CultureInfo.InvariantCulture)
        };

        var message = new StringBuilder(_templates.Value.WeeklySummary.Header);

        foreach (var line in _templates.Value.WeeklySummary.Lines)
        {
            message.Append('\n').Append(Fill(line, values));
        }

        await _alerts.SendAsync(message.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> RaiseAsync(
        HealthAlertKind kind,
        long? sourceId,
        string message,
        CancellationToken cancellationToken)
    {
        // Most alarms describe something that could change by tomorrow, so a day
        // is the right silence. "Has produced nothing since it was switched on"
        // is not one of those: it is the same fact tomorrow, and repeating it
        // daily is how an alarm becomes something to swipe away.
        var cooldown = kind == HealthAlertKind.NeverProduced
            ? _options.NeverProducedCooldown
            : _options.Cooldown;

        if (!await _health
            .TryRaiseAsync(kind, sourceId, cooldown, message, cancellationToken)
            .ConfigureAwait(false))
        {
            return 0;
        }

        _logger.LogWarning("Health alarm {Kind} for source {SourceId}: {Message}", kind, sourceId, message);

        await _alerts.SendAsync(message, cancellationToken).ConfigureAwait(false);

        return 1;
    }

    private static string Fill(string template, Dictionary<string, string> values)
    {
        var builder = new StringBuilder(template);

        foreach (var (name, value) in values)
        {
            builder.Replace("{" + name + "}", value);
        }

        return builder.ToString();
    }
}
