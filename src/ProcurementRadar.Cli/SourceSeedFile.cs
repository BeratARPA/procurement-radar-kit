using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProcurementRadar.Cli;

/// <summary>
/// Shape of config/sources.seed.json. Property names are spelled out rather than
/// left to a naming policy: this file is filled in by hand and a silent mismatch
/// would look like an empty seed.
/// </summary>
internal sealed record SourceSeedFile
{
    [JsonPropertyName("sources")]
    public IReadOnlyList<SourceSeedEntry> Sources { get; init; } = [];
}

internal sealed record SourceSeedEntry
{
    [JsonPropertyName("authority_name")]
    public string? AuthorityName { get; init; }

    [JsonPropertyName("authority_type")]
    public string? AuthorityType { get; init; }

    [JsonPropertyName("province")]
    public string? Province { get; init; }

    [JsonPropertyName("district")]
    public string? District { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("adapter_key")]
    public string? AdapterKey { get; init; }

    [JsonPropertyName("adapter_config")]
    public JsonElement? AdapterConfig { get; init; }

    [JsonPropertyName("poll_interval_minutes")]
    public int? PollIntervalMinutes { get; init; }

    [JsonPropertyName("is_active")]
    public bool? IsActive { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}
