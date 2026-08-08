using Microsoft.AspNetCore.Mvc;
using ProcurementRadar.Api.Contracts;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Notifications;
using ProcurementRadar.Domain.Repositories;

namespace ProcurementRadar.Api.Controllers;

/// <summary>The pages being polled, and their health.</summary>
[ApiController]
[Route("api/sources")]
[Produces("application/json")]
public sealed class SourcesController : ControllerBase
{
    private readonly ISourceRepository _sources;
    private readonly ICommandQueries _queries;
    private readonly ILogger<SourcesController> _logger;

    public SourcesController(
        ISourceRepository sources,
        ICommandQueries queries,
        ILogger<SourcesController> logger)
    {
        _sources = sources;
        _queries = queries;
        _logger = logger;
    }

    /// <summary>Sources currently in the rotation.</summary>
    [HttpGet(Name = "listActiveSources")]
    [ProducesResponseType(typeof(IReadOnlyList<Source>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<Source>>> ListActive(CancellationToken cancellationToken)
        => Ok(await _sources.ListActiveAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Every source, switched-off ones included.
    /// </summary>
    /// <remarks>
    /// The collector only ever wants the active ones; a person wants all of them.
    /// Twenty-five are registered and off, each with its reason in its notes, and
    /// one was switched off automatically after ten consecutive failures with no
    /// way to see it afterwards.
    /// </remarks>
    [HttpGet("all", Name = "listAllSources")]
    [ProducesResponseType(typeof(IReadOnlyList<Source>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<Source>>> ListAll(CancellationToken cancellationToken)
        => Ok(await _sources.ListAllAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>Sources that are failing or have gone quiet.</summary>
    [HttpGet("troubled", Name = "listTroubledSources")]
    [ProducesResponseType(typeof(IReadOnlyList<SourceHealth>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SourceHealth>>> Troubled(CancellationToken cancellationToken)
        => Ok(await _queries.ListTroubledSourcesAsync(30, cancellationToken).ConfigureAwait(false));

    [HttpGet("{id:long}", Name = "getSource")]
    [ProducesResponseType(typeof(Source), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Source>> Get(long id, CancellationToken cancellationToken)
    {
        var source = await _sources.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return source is null ? NotFound() : Ok(source);
    }

    /// <summary>
    /// Puts a source in or out of the rotation.
    /// </summary>
    /// <remarks>
    /// Activating clears the failure count as well. Leaving it at ten would take
    /// the source straight back out on its next stumble, and the reason it failed
    /// ten times is usually the thing that was just fixed.
    /// </remarks>
    [HttpPost("{id:long}/active", Name = "setSourceActive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetActive(
        long id,
        [FromBody] ActiveChangeRequest request,
        CancellationToken cancellationToken)
    {
        if (request.IsActive)
        {
            await _sources.ActivateAsync(id, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _sources.DeactivateAsync(id, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Source {SourceId} was switched {State} from the interface",
            id,
            request.IsActive ? "on" : "off");

        return NoContent();
    }
}
