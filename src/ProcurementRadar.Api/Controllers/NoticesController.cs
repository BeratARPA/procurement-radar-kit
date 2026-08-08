using Microsoft.AspNetCore.Mvc;
using ProcurementRadar.Api.Contracts;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Notifications;
using ProcurementRadar.Domain.Repositories;

namespace ProcurementRadar.Api.Controllers;

/// <summary>The notices themselves.</summary>
[ApiController]
[Route("api/notices")]
[Produces("application/json")]
public sealed class NoticesController : ControllerBase
{
    private readonly ICommandQueries _queries;
    private readonly INoticeRepository _notices;

    public NoticesController(ICommandQueries queries, INoticeRepository notices)
    {
        _queries = queries;
        _notices = notices;
    }

    /// <summary>Open notices, or the whole archive when a term is given.</summary>
    /// <param name="q">Searches title and authority. Without it the answer is what is still open.</param>
    /// <param name="minScore">Only used for the open list.</param>
    /// <param name="limit">Capped at 200.</param>
    [HttpGet(Name = "listNotices")]
    [ProducesResponseType(typeof(IReadOnlyList<CommandNotice>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CommandNotice>>> List(
        [FromQuery] string? q,
        [FromQuery] int? minScore,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit ?? 50, 1, 200);

        var notices = string.IsNullOrWhiteSpace(q)
            ? await _queries.ListOpenAsync(minScore ?? 0, take, cancellationToken).ConfigureAwait(false)
            : await _queries.SearchAsync(q, take, cancellationToken).ConfigureAwait(false);

        return Ok(notices);
    }

    /// <summary>Notices closing within the window, soonest first.</summary>
    [HttpGet("closing", Name = "listClosingNotices")]
    [ProducesResponseType(typeof(IReadOnlyList<CommandNotice>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CommandNotice>>> Closing(
        [FromQuery] int? withinHours,
        CancellationToken cancellationToken)
        => Ok(await _queries
            .ListClosingSoonAsync(Math.Clamp(withinHours ?? 48, 1, 720), 100, cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// One notice by its own id, announcement text included.
    /// </summary>
    /// <remarks>
    /// This is the one a list links to. Only EKAP issues an IKN, so most of the
    /// archive has none - 814 of the first 924 notices - and a screen keyed on the
    /// IKN could open about one notice in eight.
    /// </remarks>
    /// <param name="id">The id every list row carries.</param>
    [HttpGet("by-id/{id:long}", Name = "getNotice")]
    [ProducesResponseType(typeof(NoticeDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NoticeDetail>> DetailById(long id, CancellationToken cancellationToken)
    {
        var detail = await _queries.GetDetailByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return detail is null ? NotFound() : Ok(detail);
    }

    /// <summary>One notice by its tender registration number, announcement text included.</summary>
    /// <param name="ikn">For example 2026/1166872.</param>
    [HttpGet("{ikn}", Name = "getNoticeByIkn")]
    [ProducesResponseType(typeof(NoticeDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NoticeDetail>> Detail(string ikn, CancellationToken cancellationToken)
    {
        var detail = await _queries.GetDetailAsync(ikn, cancellationToken).ConfigureAwait(false);

        return detail is null ? NotFound() : Ok(detail);
    }

    /// <summary>
    /// Records the team's verdict on a notice.
    /// </summary>
    /// <remarks>
    /// These verdicts are what makes the threshold measurable later (spec section
    /// 10): if what was dismissed and what was pursued score the same, the filter
    /// is not discriminating.
    /// </remarks>
    [HttpPost("{id:long}/status", Name = "changeNoticeStatus")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangeStatus(
        long id,
        [FromBody] StatusChangeRequest request,
        CancellationToken cancellationToken)
    {
        // The interface sends what the schema stores - not_relevant - and the
        // enum spells the same thing without the underscore.
        if (!Enum.TryParse<NoticeStatus>(
                request.Status.Replace("_", string.Empty, StringComparison.Ordinal),
                ignoreCase: true,
                out var status))
        {
            return Problem(
                detail: $"'{request.Status}' is not a status.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        await _notices.UpdateStatusAsync(id, status, cancellationToken).ConfigureAwait(false);

        return NoContent();
    }
}
