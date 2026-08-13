using Microsoft.Extensions.Logging;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Adapters;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Fetching;
using ProcurementRadar.Domain.Health;
using ProcurementRadar.Domain.Notifications;
using ProcurementRadar.Domain.Repositories;
using ProcurementRadar.Domain.Settings;

namespace ProcurementRadar.Notifier;

public sealed record QuarantineSweepSummary(int Probed, int Recovered);

/// <summary>
/// Asks switched-off sources whether the thing that switched them off is still
/// true.
/// </summary>
/// <remarks>
/// The gap this closes was measured, not imagined. ANFA A.Ş. went out of the
/// rotation on 8 Aug 2026 because the authority's hosting account was suspended.
/// The note recorded at the time ended "when the site is back the only job is
/// is_active = true". The site came back and nothing looked again for three days,
/// because <c>ListActive</c> does not return switched-off sources - so the source
/// could never be fetched, so it could never succeed, so the counter that took it
/// out could never clear. A one-way door.
///
/// Recovery is judged on notices, not on a status code. A parked domain, a
/// suspended-account page and a login wall all answer 200 cheerfully; what none
/// of them do is parse into a tender. Requiring at least one notice is also what
/// makes the alarm worth reading - it says the source is collecting again, not
/// that a socket opened.
///
/// Nothing here writes to the archive or the notice table. The probe decides one
/// thing - is this source alive - and leaves collecting to the collector, which
/// will pick the source up on its next sweep once it is back in the rotation.
/// </remarks>
public sealed class QuarantineSweeper
{
    // A recovery is rare, so the cooldown is really only a guard against a source
    // that flaps in and out on consecutive sweeps.
    private static readonly TimeSpan AlertCooldown = TimeSpan.FromHours(24);

    private readonly ISourceRepository _sources;
    private readonly ISourceAdapterRegistry _adapters;
    private readonly IHealthRepository _health;
    private readonly IAlertChannel _alerts;
    private readonly ICurrent<MessageTemplates> _templates;
    private readonly ILogger<QuarantineSweeper> _logger;

    public QuarantineSweeper(
        ISourceRepository sources,
        ISourceAdapterRegistry adapters,
        IHealthRepository health,
        IAlertChannel alerts,
        ICurrent<MessageTemplates> templates,
        ILogger<QuarantineSweeper> logger)
    {
        _sources = sources;
        _adapters = adapters;
        _health = health;
        _alerts = alerts;
        _templates = templates;
        _logger = logger;
    }

    public async Task<QuarantineSweepSummary> RunAsync(int batchSize, CancellationToken cancellationToken)
    {
        var quarantined = await _sources
            .ListQuarantinedAsync(batchSize, cancellationToken)
            .ConfigureAwait(false);

        if (quarantined.Count == 0)
        {
            return new QuarantineSweepSummary(0, 0);
        }

        var probed = 0;
        var recovered = 0;

        foreach (var source in quarantined)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Stamped before the attempt, not after. A source whose fetch throws
            // would otherwise keep its old timestamp, stay at the head of the
            // queue, and be the only source this sweep ever tries again.
            await _sources
                .RecordQuarantineProbeAsync(source.Id, cancellationToken)
                .ConfigureAwait(false);

            probed++;

            var count = await CountAsync(source, cancellationToken).ConfigureAwait(false);

            if (count <= 0)
            {
                continue;
            }

            await _sources.ActivateAsync(source.Id, cancellationToken).ConfigureAwait(false);

            recovered++;

            _logger.LogInformation(
                "Source {SourceId} ({Authority}) answered again with {Count} notice(s) and is back in the rotation",
                source.Id,
                source.AuthorityName,
                count);

            await AnnounceAsync(source, count, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Quarantine sweep: {Probed} source(s) probed, {Recovered} recovered",
            probed,
            recovered);

        return new QuarantineSweepSummary(probed, recovered);
    }

    /// <summary>
    /// How many notices this source's page would yield right now, or zero.
    /// </summary>
    /// <remarks>
    /// Deliberately swallows everything. A quarantined source is by definition
    /// one that was failing, so a probe that throws is the expected case and not
    /// worth an ERROR line every day for a site that has been gone for a month.
    /// </remarks>
    private async Task<int> CountAsync(Source source, CancellationToken cancellationToken)
    {
        var adapter = _adapters.For(source.AdapterKey);

        if (adapter is null)
        {
            _logger.LogWarning(
                "Quarantined source {SourceId} wants adapter '{AdapterKey}', which nothing provides",
                source.Id,
                source.AdapterKey);

            return 0;
        }

        // No selectors, no recovery. Every adapter here needs a config, so a
        // source without one parses to nothing however well its server answers -
        // the probe would be a request a day that cannot change the outcome, and
        // the parser would log an ERROR each time for a state that is expected.
        //
        // Said out loud instead, because this one really does need a person: the
        // page has to be looked at and selectors written before coming back is
        // even possible.
        if (string.IsNullOrWhiteSpace(source.AdapterConfig))
        {
            _logger.LogWarning(
                "Quarantined source {SourceId} ({Authority}) has no selectors, so it cannot come back on its own",
                source.Id,
                source.AuthorityName);

            return 0;
        }

        try
        {
            // No conditional headers: the point is to see the page, and an ETag
            // from before the outage would earn a 304 with nothing to parse.
            var result = await adapter
                .FetchAsync(source, ConditionalRequest.None, cancellationToken)
                .ConfigureAwait(false);

            if (result.Outcome != FetchOutcome.Fetched || result.Body is not { Length: > 0 } bytes)
            {
                return 0;
            }

            // The same decoding the parse pipeline does (D-11), so a page served
            // as windows-1254 is judged here the way it will be judged in
            // production rather than coming back as replacement characters.
            var (body, _) = BodyDecoder.Decode(bytes, CharSetFrom(result.ContentType));

            return adapter.Parse(body, source).Count;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Quarantined source {SourceId} is still not answering",
                source.Id);

            return 0;
        }
    }

    private async Task AnnounceAsync(Source source, int count, CancellationToken cancellationToken)
    {
        var template = _templates.Value.Alerts.SourceRecovered;

        if (string.IsNullOrWhiteSpace(template))
        {
            return;
        }

        var message = template
            .Replace("{authority}", source.AuthorityName, StringComparison.Ordinal)
            .Replace("{count}", count.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(
                "{reason}",
                source.DeactivatedReason is { } reason ? EnumText.ToText(reason) : "-",
                StringComparison.Ordinal);

        try
        {
            // Through the health table like every other alarm, so the recovery
            // shows up in the same history the screen reads.
            if (!await _health
                .TryRaiseAsync(HealthAlertKind.SourceRecovered, source.Id, AlertCooldown, message, cancellationToken)
                .ConfigureAwait(false))
            {
                return;
            }

            _logger.LogInformation(
                "Health alarm {Kind} for source {SourceId}: {Message}",
                HealthAlertKind.SourceRecovered,
                source.Id,
                message);

            await _alerts.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The source is already back in the rotation, which is the part that
            // matters. A telegram outage must not undo that.
            _logger.LogError(exception, "Could not announce the recovery of source {SourceId}", source.Id);
        }
    }

    private static string? CharSetFrom(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;

        var marker = contentType.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);

        if (marker < 0) return null;

        var value = contentType[(marker + "charset=".Length)..].Trim().Trim('"');
        var end = value.IndexOf(';');

        return end < 0 ? value : value[..end].Trim();
    }
}
