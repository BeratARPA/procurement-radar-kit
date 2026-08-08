using Microsoft.AspNetCore.Mvc;
using ProcurementRadar.Domain.Notifications;

namespace ProcurementRadar.Api.Controllers;

/// <summary>What the system is doing right now.</summary>
[ApiController]
[Route("api/health")]
[Produces("application/json")]
public sealed class HealthController : ControllerBase
{
    private readonly ICommandQueries _queries;

    public HealthController(ICommandQueries queries)
    {
        _queries = queries;
    }

    /// <summary>Active sources, failing sources, notice counts and the last fetch.</summary>
    [HttpGet(Name = "getHealth")]
    [ProducesResponseType(typeof(SystemStatus), StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemStatus>> Get(CancellationToken cancellationToken)
        => Ok(await _queries.GetStatusAsync(cancellationToken).ConfigureAwait(false));
}
