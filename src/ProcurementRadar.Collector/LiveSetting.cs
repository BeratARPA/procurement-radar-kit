using Microsoft.Extensions.Logging;
using ProcurementRadar.Domain.Settings;

namespace ProcurementRadar.Collector;

/// <summary>
/// One setting held in memory, replaced when the stored json changes.
/// </summary>
/// <remarks>
/// Generic over how the json becomes an object, which is what lets this live
/// here rather than in a project that knows about keyword rules and message
/// templates. The host supplies the loader it already has - KeywordRules.Load,
/// MessageTemplates.Load - and this only decides when to call it.
/// </remarks>
public abstract class LiveSetting
{
    public abstract string Key { get; }

    /// <summary>
    /// Replaces the value, or keeps the old one and says why it could not.
    /// </summary>
    public abstract bool TryApply(string json, out string? problem);
}

public sealed class LiveSetting<T> : LiveSetting, ICurrent<T>
    where T : class
{
    private readonly Func<string, T> _load;
    private volatile T _value;

    /// <param name="fallback">
    /// What to use until the database answers. The value read from the config
    /// file at startup, so a worker that cannot reach the setting table yet
    /// still collects, parses and scores rather than refusing to start.
    /// </param>
    public LiveSetting(string key, Func<string, T> load, T fallback)
    {
        Key = key;
        _load = load;
        _value = fallback;
    }

    public override string Key { get; }

    public T Value => _value;

    public override bool TryApply(string json, out string? problem)
    {
        try
        {
            _value = _load(json);
            problem = null;

            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Deliberately broad. What arrives is json somebody typed into a
            // form, and every loader here fails differently at it. The old value
            // stays in place: a system running on yesterday's keyword list is
            // working, one running on none is not.
            problem = exception.Message;

            return false;
        }
    }
}

/// <summary>
/// Asks whether any setting changed and refreshes the ones that did.
/// </summary>
/// <remarks>
/// One max(updated_at) over a table of a dozen rows, every thirty seconds. Cheap
/// enough to ask often, and it answers the only question worth asking - is
/// anything I am holding stale - without reading a row that has not moved.
/// </remarks>
public sealed class SettingsRefresher
{
    private readonly ISettingRepository _settings;
    private readonly IReadOnlyList<LiveSetting> _live;
    private readonly ILogger<SettingsRefresher> _logger;

    private DateTimeOffset? _seen;

    public SettingsRefresher(
        ISettingRepository settings,
        IEnumerable<LiveSetting> live,
        ILogger<SettingsRefresher> logger)
    {
        _settings = settings;
        _live = live.ToList();
        _logger = logger;
    }

    /// <summary>Returns how many settings were replaced.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var version = await _settings.GetVersionAsync(cancellationToken).ConfigureAwait(false);

        if (version is null || version == _seen)
        {
            return 0;
        }

        var stored = await _settings.ListAsync(cancellationToken).ConfigureAwait(false);
        var byKey = stored.ToDictionary(setting => setting.Key, setting => setting.Value, StringComparer.Ordinal);
        var applied = 0;

        foreach (var live in _live)
        {
            if (!byKey.TryGetValue(live.Key, out var json))
            {
                continue;
            }

            if (live.TryApply(json, out var problem))
            {
                applied++;
            }
            else
            {
                // Loud, because nothing else will notice. The system keeps
                // running on the previous value and looks entirely healthy while
                // doing it, which is exactly how a broken setting stays broken
                // for a fortnight.
                _logger.LogError(
                    "Setting {Key} could not be loaded and the previous value is still in use: {Problem}",
                    live.Key,
                    problem);
            }
        }

        // Marked as seen even when something failed. Re-reading the same json
        // thirty seconds later fails identically; the next real change gets
        // another attempt, and a bad value has already been logged as an error.
        _seen = version;

        _logger.LogInformation("Settings refreshed: {Applied} of {Total} reloaded", applied, _live.Count);

        return applied;
    }
}
