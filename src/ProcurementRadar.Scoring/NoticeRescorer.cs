using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Notifications;
using ProcurementRadar.Domain.Repositories;

namespace ProcurementRadar.Scoring;

/// <summary>What a rescore did.</summary>
public sealed record RescoreResult
{
    public required int Considered { get; init; }

    /// <summary>How many ended up with a different number than before.</summary>
    public required int Changed { get; init; }

    /// <summary>How many crossed up over the notification threshold.</summary>
    public required int Gained { get; init; }

    /// <summary>How many fell below it.</summary>
    public required int Lost { get; init; }

    public required int Threshold { get; init; }

    /// <summary>The biggest movers, for a person to sanity-check.</summary>
    public required IReadOnlyList<RescoredNotice> Notable { get; init; }

    /// <summary>
    /// Everything that fell below the threshold, in full.
    /// </summary>
    /// <remarks>
    /// Kept apart from the movers rather than sorted in with them. The movers are
    /// ordered by how far they rose, so a notice that quietly went silent sorts
    /// to the bottom and is cut off - and going silent is the half of a keyword
    /// change worth checking. There are usually a handful, so none are dropped.
    /// </remarks>
    public required IReadOnlyList<RescoredNotice> Dropped { get; init; }
}

public sealed record RescoredNotice
{
    public required long NoticeId { get; init; }

    public required string Title { get; init; }

    public required int Before { get; init; }

    public required int After { get; init; }
}

/// <summary>
/// Runs the current keyword list over everything already collected.
/// </summary>
/// <remarks>
/// A score is written once, when the notice is first parsed. That is right for
/// collection and wrong after a keyword change: the words added today only ever
/// reach notices that arrive tomorrow, so the archive keeps the verdict of a
/// list that no longer exists. Adding "crm" and leaving a CRM tender at zero is
/// the whole shape of the problem.
///
/// Nothing is fetched and nothing is parsed - this reads the notices as they are
/// stored and rewrites `notice_score`. `llm_score` is untouched because the
/// repository leaves it alone (D-03 keeps it null in v1, and once something does
/// fill it, re-running the cheap filter must not wipe the expensive answer).
/// </remarks>
public sealed class NoticeRescorer
{
    private readonly ICommandQueries _queries;
    private readonly INoticeScoreRepository _scores;
    private readonly TimeProvider _timeProvider;

    public NoticeRescorer(
        ICommandQueries queries,
        INoticeScoreRepository scores,
        TimeProvider timeProvider)
    {
        _queries = queries;
        _scores = scores;
        _timeProvider = timeProvider;
    }

    public async Task<RescoreResult> RescoreAsync(
        RuleScorer scorer,
        int limit,
        CancellationToken cancellationToken)
    {
        var notices = await _queries.ListForRescoreAsync(limit, cancellationToken).ConfigureAwait(false);

        var threshold = scorer.Rules.NotifyThreshold;
        var now = _timeProvider.GetUtcNow();

        var changed = 0;
        var gained = 0;
        var notable = new List<RescoredNotice>();
        var dropped = new List<RescoredNotice>();

        foreach (var notice in notices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var scored = scorer.Score(notice.Title, notice.Description, notice.OkasCodes);
            var before = notice.CurrentScore;

            if (scored.Value != before)
            {
                changed++;

                var moved = new RescoredNotice
                {
                    NoticeId = notice.NoticeId,
                    Title = notice.Title,
                    Before = before,
                    After = scored.Value
                };

                if (before < threshold && scored.Value >= threshold) gained++;
                if (before >= threshold && scored.Value < threshold) dropped.Add(moved);

                notable.Add(moved);
            }

            // Written whether or not it moved. A notice scored under an older
            // model should carry today's model name even when today's list
            // happens to agree with yesterday's.
            await _scores.UpsertAsync(
                new NoticeScore
                {
                    NoticeId = notice.NoticeId,
                    RuleScore = scored.Value,
                    Rationale = scored.Rationale,
                    Model = RuleScorer.ModelName,
                    ScoredAt = now
                },
                cancellationToken).ConfigureAwait(false);
        }

        return new RescoreResult
        {
            Considered = notices.Count,
            Changed = changed,
            Gained = gained,
            Lost = dropped.Count,
            Threshold = threshold,
            // Biggest gains first: what a keyword change was meant to find is
            // more interesting than what it happened to demote.
            Notable = notable
                .OrderByDescending(row => row.After - row.Before)
                .Take(25)
                .ToList(),
            Dropped = dropped
                .OrderBy(row => row.After)
                .ToList()
        };
    }
}
