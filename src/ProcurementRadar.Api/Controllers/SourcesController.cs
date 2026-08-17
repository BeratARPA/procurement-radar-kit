using Microsoft.AspNetCore.Mvc;
using ProcurementRadar.Api.Contracts;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Notifications;
using ProcurementRadar.Domain.Repositories;
using ProcurementRadar.Parsers;

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
    /// Registers a source, or refreshes the definition of the one at that url.
    /// </summary>
    /// <remarks>
    /// A new source is created **switched off**. spec Bolum 5 ends its
    /// procedure with three days of observation before `is_active = true`, and a
    /// screen that skipped straight to polling would skip the one step that
    /// catches a bad selector before it burns politeness budget. Switching it on
    /// is a separate, deliberate press.
    /// </remarks>
    [HttpPost(Name = "saveSource")]
    [ProducesResponseType(typeof(SourceSavedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SourceSavedResponse>> Save(
        [FromBody] SourceDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        if (Describe(request, out var source, out var problem) is false)
        {
            return Problem(detail: problem, statusCode: StatusCodes.Status400BadRequest);
        }

        var saved = await _sources
            .SaveDefinitionAsync(source! with { IsActive = false }, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Source {SourceId} was {Action} from the interface: {Url}",
            saved.Id,
            saved.WasCreated ? "registered" : "redefined",
            request.Url);

        return Ok(new SourceSavedResponse { Id = saved.Id, WasCreated = saved.WasCreated });
    }

    /// <summary>
    /// Edits a source that already exists, url included.
    /// </summary>
    /// <remarks>
    /// Leaves the rotation and the health columns alone: editing a selector must
    /// not be able to switch a source back on or clear the failure count that
    /// took it out.
    /// </remarks>
    [HttpPut("{id:long}", Name = "updateSource")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        long id,
        [FromBody] SourceDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        if (Describe(request, out var source, out var problem) is false)
        {
            return Problem(detail: problem, statusCode: StatusCodes.Status400BadRequest);
        }

        var updated = await _sources
            .UpdateDefinitionAsync(id, source!, cancellationToken)
            .ConfigureAwait(false);

        if (!updated) return NotFound();

        _logger.LogInformation("Source {SourceId} was edited from the interface", id);

        return NoContent();
    }

    /// <summary>
    /// What a set of selectors would find on a page.
    /// </summary>
    /// <remarks>
    /// Nothing is fetched and nothing is written. The page comes from the
    /// archive - the one the collector actually got - or from the request, so
    /// trying a selector costs the authority nothing.
    /// </remarks>
    [HttpPost("check", Name = "checkSource")]
    [ProducesResponseType(typeof(SourceCheckResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SourceCheckResponse>> Check(
        [FromBody] SourceCheckRequest request,
        SourceChecker checker,
        CancellationToken cancellationToken)
        => Ok(await checker.CheckAsync(request, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Turns a request into a source, or says why it cannot.
    /// </summary>
    /// <remarks>
    /// The adapter config is checked here rather than left to fail at the next
    /// poll. A source stored with selectors nothing can read is a source that
    /// fetches politely, parses to nothing, and looks healthy while doing it.
    /// </remarks>
    private static bool Describe(
        SourceDefinitionRequest request,
        out Source? source,
        out string? problem)
    {
        source = null;
        problem = null;

        if (!Enum.TryParse<AuthorityType>(
                request.AuthorityType.Replace("_", string.Empty, StringComparison.Ordinal),
                ignoreCase: true,
                out var authorityType))
        {
            problem = $"'{request.AuthorityType}' is not an authority type.";
            return false;
        }

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            problem = $"'{request.Url}' is not an http address.";
            return false;
        }

        if (request.PollIntervalMinutes < 1)
        {
            problem = "poll_interval_minutes must be at least 1.";
            return false;
        }

        // An absent config is allowed; a broken one is not.
        //
        // The rule used to be "generic_table must have a usable config", which
        // reads sensibly and locked out the exact sources that needed editing:
        // thirteen are switched off *because* nobody has written their selectors
        // yet, and this refused to save them - so a note explaining why one is
        // off could not be written from the interface either. The 11 August
        // audit had to put those notes in with hand SQL, which is what D-16
        // exists to stop.
        //
        // Nothing is lost by allowing it. A source with no config collects
        // nothing whether or not the api says so, it cannot be switched on
        // usefully, and preflight already names any active source in that state.
        // Text that is present and does not parse is still refused, because that
        // is a typo somebody wants to hear about while they are looking at it.
        if (request.AdapterKey == GenericTableAdapter.AdapterKey
            && !IsBlankConfig(request.AdapterConfig)
            && !GenericTableAdapterConfig.TryRead(request.AdapterConfig!, out _, out var configProblem))
        {
            problem = configProblem;
            return false;
        }

        source = new Source
        {
            AuthorityName = request.AuthorityName,
            AuthorityType = authorityType,
            Province = request.Province,
            District = request.District,
            Url = request.Url,
            AdapterKey = request.AdapterKey,
            AdapterConfig = request.AdapterConfig,
            PollIntervalMinutes = request.PollIntervalMinutes,
            Notes = request.Notes
        };

        return true;
    }

    /// <summary>
    /// Nothing worth validating: absent, empty, or an empty json object.
    /// </summary>
    /// <remarks>
    /// The three spellings all mean the same thing to the collector, and all
    /// three are in the table today - preflight counts a generic_table source
    /// unconfigured on exactly this test.
    /// </remarks>
    private static bool IsBlankConfig(string? adapterConfig)
        => string.IsNullOrWhiteSpace(adapterConfig)
            || adapterConfig.Trim() is "{}" or "null";

    /// <summary>
    /// Puts a source in or out of the rotation.
    /// </summary>
    /// <remarks>
    /// Activating clears the failure count as well. Leaving it at ten would take
    /// the source straight back out on its next stumble, and the reason it failed
    /// ten times is usually the thing that was just fixed.
    ///
    /// Switching one off records a reason, which is what decides whether the
    /// quarantine sweep will ever ask it again.
    /// </remarks>
    [HttpPost("{id:long}/active", Name = "setSourceActive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
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
            if (!TryReadReason(request.Reason, out var reason))
            {
                return Problem(
                    detail: $"'{request.Reason}' is not a deactivation reason.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            await _sources.DeactivateAsync(id, reason, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Source {SourceId} was switched {State} from the interface",
            id,
            request.IsActive ? "on" : "off");

        return NoContent();
    }

    /// <summary>
    /// Reads the reason a switch-off carries, defaulting to <c>manual</c>.
    /// </summary>
    /// <remarks>
    /// <c>health_rule</c> is deliberately not accepted here. It means "nobody
    /// chose this, ten failures did", and a person pressing a button in the
    /// interface is the opposite of that.
    /// </remarks>
    private static bool TryReadReason(string? text, out DeactivationReason reason)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            reason = DeactivationReason.Manual;
            return true;
        }

        return EnumText.TryParse(text, out reason) && reason != DeactivationReason.HealthRule;
    }
}
