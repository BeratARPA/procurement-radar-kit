namespace ProcurementRadar.Domain.Settings;

/// <summary>One setting, as stored. The value is the json text of the file it replaced.</summary>
public sealed record Setting
{
    public string Key { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; init; }

    public string? UpdatedBy { get; init; }
}

/// <summary>
/// The settings that used to live in config/*.json (D-16).
/// </summary>
public interface ISettingRepository
{
    Task<IReadOnlyList<Setting>> ListAsync(CancellationToken cancellationToken);

    Task<Setting?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// When any setting last changed, or null when there are none.
    /// </summary>
    /// <remarks>
    /// This is what the worker asks every thirty seconds. One indexed max() over
    /// a table of a dozen rows is cheap enough to ask often and specific enough
    /// to answer the only question worth asking: is anything I am holding stale.
    /// </remarks>
    Task<DateTimeOffset?> GetVersionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes a setting and files the previous value in the history.
    /// </summary>
    Task SaveAsync(string key, string json, string? updatedBy, CancellationToken cancellationToken);

    /// <summary>
    /// Writes only if the key is absent. How config files seed a fresh database
    /// without overwriting a setting somebody has since changed.
    /// </summary>
    Task<bool> InsertIfMissingAsync(string key, string json, CancellationToken cancellationToken);

    Task<IReadOnlyList<Setting>> ListHistoryAsync(string key, int limit, CancellationToken cancellationToken);
}
