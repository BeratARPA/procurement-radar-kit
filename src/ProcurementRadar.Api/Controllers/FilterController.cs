using Microsoft.AspNetCore.Mvc;
using ProcurementRadar.Api.Contracts;
using ProcurementRadar.Domain.Normalization;
using ProcurementRadar.Domain.Notifications;
using ProcurementRadar.Domain.Repositories;
using ProcurementRadar.Domain.Settings;
using ProcurementRadar.Scoring;

namespace ProcurementRadar.Api.Controllers;

/// <summary>Trying a filter against the archive before living with it.</summary>
[ApiController]
[Route("api/filter")]
[Produces("application/json")]
public sealed class FilterController : ControllerBase
{
    private readonly ICommandQueries _queries;
    private readonly NoticeNormalizer _normalizer;
    private readonly ISettingRepository _settings;
    private readonly NoticeRescorer _rescorer;

    public FilterController(
        ICommandQueries queries,
        NoticeNormalizer normalizer,
        ISettingRepository settings,
        NoticeRescorer rescorer)
    {
        _queries = queries;
        _normalizer = normalizer;
        _settings = settings;
        _rescorer = rescorer;
    }

    /// <summary>
    /// What this keyword list and threshold would have announced.
    /// </summary>
    /// <remarks>
    /// D-14 moved the threshold from 60 to 30 by reading a sample by hand. The
    /// same question is a second's work here and exact: against the archive as it
    /// stands, 60 announces 2 notices, 30 announces 14, 25 announces 19.
    ///
    /// Nothing is written. The scorer is built from the submitted list and thrown
    /// away, so a preview cannot change what the worker is doing - which would be
    /// the opposite of a preview.
    /// </remarks>
    [HttpPost("preview", Name = "previewFilter")]
    [ProducesResponseType(typeof(FilterPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<FilterPreviewResponse>> Preview(
        [FromBody] FilterPreviewRequest request,
        CancellationToken cancellationToken)
    {
        if (SettingValidator.Validate(SettingKeys.Keywords, request.Keywords) is { } problem)
        {
            return Problem(detail: problem, statusCode: StatusCodes.Status400BadRequest);
        }

        var rules = KeywordRules.Load(request.Keywords);
        var threshold = request.Threshold ?? rules.NotifyThreshold;
        var scorer = new RuleScorer(new Fixed<KeywordRules>(rules), _normalizer);

        var notices = await _queries
            .ListForPreviewAsync(Math.Clamp(request.Limit ?? 1000, 1, 5000), cancellationToken)
            .ConfigureAwait(false);

        var scored = notices
            .Select(notice => new ScoredNotice
            {
                NoticeId = notice.NoticeId,
                Title = notice.Title,
                AuthorityName = notice.AuthorityName,
                Score = scorer.Score(notice.Title, notice.Description, notice.OkasCodes).Value
            })
            .ToList();

        return Ok(new FilterPreviewResponse
        {
            Considered = scored.Count,
            WouldNotify = scored.Count(row => row.Score >= threshold),
            Threshold = threshold,
            Caught = scored
                .Where(row => row.Score >= threshold)
                .OrderByDescending(row => row.Score)
                .Take(25)
                .ToList(),
            // What a filter just misses is where the threshold is actually
            // decided, so it is returned beside what it caught rather than left
            // for somebody to go looking for.
            Missed = scored
                .Where(row => row.Score > 0 && row.Score < threshold)
                .OrderByDescending(row => row.Score)
                .Take(25)
                .ToList()
        });
    }

    /// <summary>
    /// Score the whole archive again with the keyword list as it is stored now.
    /// </summary>
    /// <remarks>
    /// A notice is scored once, when it is parsed. So a word added today only
    /// ever reaches notices that arrive tomorrow, and everything already
    /// collected keeps the verdict of a list that no longer exists. It is not a
    /// hypothetical: the day 26 words were added, a CRM tender and an OCR tender
    /// the new list matches went on showing 0.
    ///
    /// Unlike the preview this writes. It takes no keyword list in the body on
    /// purpose - it applies what is stored, which is what the worker is using,
    /// so the archive and the worker cannot end up disagreeing. To try a list
    /// first, that is what the preview is for.
    ///
    /// `llm_score` is left where it is (D-03 keeps it null in v1; once something
    /// fills it, re-running the cheap filter must not wipe the expensive answer).
    /// </remarks>
    [HttpPost("rescore", Name = "rescoreArchive")]
    [ProducesResponseType(typeof(RescoreResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RescoreResponse>> Rescore(
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var stored = await _settings.GetAsync(SettingKeys.Keywords, cancellationToken).ConfigureAwait(false);

        if (stored is null)
        {
            return Problem(
                detail: $"Setting '{SettingKeys.Keywords}' is not in the table yet - run the seeder first.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var scorer = new RuleScorer(new Fixed<KeywordRules>(KeywordRules.Load(stored.Value)), _normalizer);

        var result = await _rescorer
            .RescoreAsync(scorer, Math.Clamp(limit ?? 5000, 1, 20000), cancellationToken)
            .ConfigureAwait(false);

        return Ok(new RescoreResponse
        {
            Considered = result.Considered,
            Changed = result.Changed,
            Gained = result.Gained,
            Lost = result.Lost,
            Threshold = result.Threshold,
            Notable = result.Notable.Select(Moved).ToList(),
            Dropped = result.Dropped.Select(Moved).ToList()
        });

        static MovedNotice Moved(RescoredNotice row) => new()
        {
            NoticeId = row.NoticeId,
            Title = row.Title,
            Before = row.Before,
            After = row.After
        };
    }
}
