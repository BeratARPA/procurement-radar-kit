using Microsoft.AspNetCore.Mvc;
using ProcurementRadar.Api.Contracts;
using ProcurementRadar.Domain.Settings;

namespace ProcurementRadar.Api.Controllers;

/// <summary>Everything the system can be told, and every value it used to have.</summary>
[ApiController]
[Route("api/settings")]
[Produces("application/json")]
public sealed class SettingsController : ControllerBase
{
    private readonly ISettingRepository _settings;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(ISettingRepository settings, ILogger<SettingsController> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Every setting, with when it last changed and by whom.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<Setting>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<Setting>>> List(CancellationToken cancellationToken)
        => Ok(await _settings.ListAsync(cancellationToken).ConfigureAwait(false));

    /// <param name="key">keywords, messages, date_patterns, normalization, personal_data, award_labels, ekap_ui or politeness.</param>
    [HttpGet("{key}")]
    [ProducesResponseType(typeof(Setting), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Setting>> Get(string key, CancellationToken cancellationToken)
    {
        var setting = await _settings.GetAsync(key, cancellationToken).ConfigureAwait(false);

        return setting is null ? NotFound() : Ok(setting);
    }

    /// <summary>
    /// Replaces a setting, filing the previous value in the history.
    /// </summary>
    /// <remarks>
    /// Validated by the loader that will read it, not by a second description of
    /// what valid looks like: two validators drift, one cannot. The politeness
    /// setting is refused below five seconds between requests to a host, and with
    /// robots.txt turned off, whoever is asking (D-16).
    ///
    /// The worker would survive a bad value - it keeps the previous one and logs
    /// an error - but the person who pressed save would never hear about it.
    /// </remarks>
    [HttpPut("{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Save(
        string key,
        [FromBody] SettingChangeRequest request,
        CancellationToken cancellationToken)
    {
        if (SettingValidator.Validate(key, request.Value) is { } problem)
        {
            return Problem(detail: problem, statusCode: StatusCodes.Status400BadRequest);
        }

        await _settings.SaveAsync(key, request.Value, request.UpdatedBy, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Setting {Key} was changed by {By}", key, request.UpdatedBy);

        return NoContent();
    }

    /// <summary>
    /// Previous values, newest first.
    /// </summary>
    /// <remarks>
    /// One mistyped keyword silences the notifications and nothing about the
    /// system looks broken while it happens. A settings screen that cannot be
    /// undone is worse than no settings screen.
    /// </remarks>
    [HttpGet("{key}/history")]
    [ProducesResponseType(typeof(IReadOnlyList<Setting>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<Setting>>> History(
        string key,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
        => Ok(await _settings
            .ListHistoryAsync(key, Math.Clamp(limit ?? 20, 1, 100), cancellationToken)
            .ConfigureAwait(false));
}
