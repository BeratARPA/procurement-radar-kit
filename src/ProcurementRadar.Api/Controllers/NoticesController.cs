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

    /// <summary>
    /// Notices, still open by default and the whole archive on request.
    /// </summary>
    /// <remarks>
    /// The open list is the right answer for a notification and a narrow one for
    /// a screen: Telegram already applies the filter, so an interface that only
    /// ever shows what passed it is a second funnel over the same water.
    /// `scope=all` hands over everything and lets the reader narrow it.
    /// </remarks>
    /// <param name="q">Searches title and authority across the whole archive.</param>
    /// <param name="scope">open (default) or all.</param>
    /// <param name="minScore">Applies to both scopes; ignored when a term is given.</param>
    /// <param name="limit">Capped at 5000.</param>
    [HttpGet(Name = "listNotices")]
    [ProducesResponseType(typeof(IReadOnlyList<CommandNotice>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CommandNotice>>> List(
        [FromQuery] string? q,
        [FromQuery] string? scope,
        [FromQuery] int? minScore,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        // 5000 rather than 200, because a screen that shows the first 200 of 988
        // and says nothing is a screen that lies by omission. It is still a cap:
        // at forty notices a day this holds several months, and when it stops
        // holding the answer is paging, not a larger number.
        var take = Math.Clamp(limit ?? 50, 1, 5000);

        if (!string.IsNullOrWhiteSpace(q))
        {
            return Ok(await _queries.SearchAsync(q, take, cancellationToken).ConfigureAwait(false));
        }

        var everything = string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase);

        var notices = everything
            ? await _queries.ListAllAsync(minScore ?? 0, take, cancellationToken).ConfigureAwait(false)
            : await _queries.ListOpenAsync(minScore ?? 0, take, cancellationToken).ConfigureAwait(false);

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
