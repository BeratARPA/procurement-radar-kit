namespace ProcurementRadar.Api.Contracts;

/// <summary>
/// The bodies the interface sends.
/// </summary>
/// <remarks>
/// Kept apart from the entities on purpose. What a caller may set is a smaller
/// thing than what the database stores, and a request type that happens to be
/// an entity quietly invites the two to drift together.
/// </remarks>
public sealed record StatusChangeRequest
{
    /// <summary>reviewed, bid_submitted, missed or not_relevant.</summary>
    /// <example>not_relevant</example>
    public required string Status { get; init; }
}

public sealed record ActiveChangeRequest
{
    /// <summary>True puts the source back in the rotation and clears its failure count.</summary>
    public required bool IsActive { get; init; }
}

public sealed record SettingChangeRequest
{
    /// <summary>The setting as json text, validated with the loader that will read it.</summary>
    public required string Value { get; init; }

    /// <summary>Who changed it. Recorded in the history beside the value.</summary>
    public string? UpdatedBy { get; init; }
}

public sealed record FilterPreviewRequest
{
    /// <summary>A keyword list in the shape of config/keywords.json.</summary>
    public required string Keywords { get; init; }

    /// <summary>Overrides the list's own notify_threshold when given.</summary>
    public int? Threshold { get; init; }

    /// <summary>How much of the archive to score. Everything, by default.</summary>
    public int? Limit { get; init; }
}
