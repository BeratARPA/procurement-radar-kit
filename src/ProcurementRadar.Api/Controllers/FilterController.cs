using Microsoft.AspNetCore.Mvc;
using ProcurementRadar.Api.Contracts;
using ProcurementRadar.Domain.Normalization;
using ProcurementRadar.Domain.Notifications;
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

    public FilterController(ICommandQueries queries, NoticeNormalizer normalizer)
    {
        _queries = queries;
        _normalizer = normalizer;
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
    [HttpPost("preview")]
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
}
