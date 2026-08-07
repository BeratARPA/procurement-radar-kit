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
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AwardSummary>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AwardSummary>>> List(
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
        => Ok(await _awards
            .ListRecentAsync(Math.Clamp(limit ?? 50, 1, 200), cancellationToken)
            .ConfigureAwait(false));
}
