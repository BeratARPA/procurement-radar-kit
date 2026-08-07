using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Fetching;
using ProcurementRadar.Domain.Repositories;
using ProcurementRadar.Parsers;

namespace ProcurementRadar.Worker;

public sealed record AwardSweepSummary(int Considered, int Recorded, int NotYet, int Failed);

/// <summary>
/// Goes back to tenders that have closed and asks EKAP how they ended.
/// </summary>
/// <remarks>
/// Everything else in this system runs forwards, towards a deadline. This one
/// runs backwards, and it is the only part that produces data about the market
/// rather than about an opportunity: what an authority estimated against what it
/// signed, and how many firms turned up. spec Bolum 16 says the archive is the
/// real asset; this is the half of the archive that was missing.
///
/// Deliberately slow. Nothing here is time-critical - a result published today is
/// as useful next week - so it runs a handful of İKNs every few hours and gets out
/// of the way of the collector, which is the process with an actual deadline.
/// </remarks>
public sealed class AwardSweeper
{
    private readonly IAwardRepository _awards;
    private readonly IEkapSearchClient _search;
    private readonly AwardParser _parser;
    private readonly ILogger<AwardSweeper> _logger;

    public AwardSweeper(
        IAwardRepository awards,
        IEkapSearchClient search,
        AwardParser parser,
        ILogger<AwardSweeper> logger)
    {
        _awards = awards;
        _search = search;
        _parser = parser;
        _logger = logger;
    }

    public async Task<AwardSweepSummary> RunAsync(
        int ripeDays,
        int maximumAttempts,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var pending = await _awards
            .ListPendingAsync(ripeDays, maximumAttempts, batchSize, cancellationToken)
            .ConfigureAwait(false);

        if (pending.Count == 0)
        {
            return new AwardSweepSummary(0, 0, 0, 0);
        }

        var recorded = 0;
        var notYet = 0;
        var failed = 0;

        foreach (var candidate in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _search
                .FetchAnnouncementAsync(candidate.Ikn, cancellationToken)
                .ConfigureAwait(false);

            if (result.Outcome != FetchOutcome.Fetched || result.Body is null)
            {
                // Not counted as an attempt. A browser that timed out says
                // nothing about whether the result exists, and spending one of
                // six attempts on it would retire the İKN for a reason that has
                // nothing to do with EKAP.
                _logger.LogWarning("No announcement came back for {Ikn}", candidate.Ikn);
                failed++;
                continue;
            }

            var text = EkapAnnouncementEnricher.PlainText(
                System.Text.Encoding.UTF8.GetString(result.Body));

            var parsed = _parser.Parse(text);

            if (parsed is null)
            {
                // The ordinary outcome for a while: the tender announcement is
                // still what comes back, because the contract has not been signed.
                // Written down anyway, so the attempt counts and this İKN stops
                // being asked about once it is clear no result is coming.
                await _awards.RecordAsync(candidate.NoticeId, null, cancellationToken).ConfigureAwait(false);
                notYet++;
                continue;
            }

            await _awards.RecordAsync(
                candidate.NoticeId,
                new Award
                {
                    NoticeId = candidate.NoticeId,
                    WinnerName = parsed.WinnerName,
                    ContractAmount = parsed.ContractAmount,
                    BidCount = parsed.BidCount,
                    AwardedAt = parsed.AwardedAt
                },
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Notice {NoticeId} ({Ikn}) was awarded to {Winner} for {Amount} against {Bids} bids",
                candidate.NoticeId,
                candidate.Ikn,
                parsed.WinnerName,
                parsed.ContractAmount,
                parsed.BidCount);

            recorded++;
        }

        _logger.LogInformation(
            "Award sweep: {Considered} asked about, {Recorded} concluded, {NotYet} still open, {Failed} unreachable",
            pending.Count,
            recorded,
            notYet,
            failed);

        return new AwardSweepSummary(pending.Count, recorded, notYet, failed);
    }
}
