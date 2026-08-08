using Microsoft.AspNetCore.Mvc;
using ProcurementRadar.Domain.Repositories;

namespace ProcurementRadar.Api.Controllers;

/// <summary>What the tenders we watched ended up as.</summary>
[ApiController]
[Route("api/awards")]
[Produces("application/json")]
public sealed class AwardsController : ControllerBase
{
    private readonly IAwardRepository _awards;

    public AwardsController(IAwardRepository awards)
    {
        _awards = awards;
    }

    /// <summary>
    /// Concluded tenders, newest first, with the estimate beside the contract.
    /// </summary>
    /// <remarks>
    /// The pair is the point: 3.405.000 estimated, 3.300.000 signed, one bid.
    /// A notice says what is wanted; only this says what it goes for.
    /// </remarks>
    [HttpGet(Name = "listAwards")]
    [ProducesResponseType(typeof(IReadOnlyList<AwardSummary>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AwardSummary>>> List(
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
        => Ok(await _awards
            .ListRecentAsync(Math.Clamp(limit ?? 50, 1, 200), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Tenders that have closed and whose result has not been read yet.
    /// </summary>
    /// <remarks>
    /// So that an empty results screen can say why it is empty. This table fills
    /// slowly on purpose - D-15 asks EKAP about five tenders every six hours,
    /// and only for notices with an IKN whose deadline passed a fortnight ago,
    /// because the result announcement goes up after the contract is signed.
    /// Without this number, a screen with nothing on it looks broken rather than
    /// early.
    /// </remarks>
    [HttpGet("pending", Name = "listPendingAwards")]
    [ProducesResponseType(typeof(IReadOnlyList<AwardCandidate>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AwardCandidate>>> Pending(
        CancellationToken cancellationToken)
        => Ok(await _awards
            .ListPendingAsync(ripeDays: 14, maximumAttempts: 6, limit: 200, cancellationToken)
            .ConfigureAwait(false));
}
