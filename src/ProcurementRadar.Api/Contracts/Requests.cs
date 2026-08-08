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

/// <summary>
/// A source definition as the interface sends it.
/// </summary>
/// <remarks>
/// The health columns are not here and cannot be: <c>last_new_notice_at</c> is
/// what the silent-source alarm reads and <c>consecutive_failures</c> is what
/// takes a source out of the rotation. Letting a form post those would make
/// editing a selector able to reset an alarm.
/// </remarks>
public sealed record SourceDefinitionRequest
{
    public required string AuthorityName { get; init; }

    /// <summary>university, municipality, provincial_directorate, hospital, soe_subsidiary or ekap.</summary>
    public required string AuthorityType { get; init; }

    public required string Province { get; init; }

    public string? District { get; init; }

    public required string Url { get; init; }

    /// <summary>generic_table, json_list, ekap_tender, ekap_direct_procurement.</summary>
    public required string AdapterKey { get; init; }

    /// <summary>The selectors, as json. Checked before it is stored.</summary>
    public string? AdapterConfig { get; init; }

    public int PollIntervalMinutes { get; init; } = 15;

    /// <summary>Why this source is here, or why it is switched off.</summary>
    public string? Notes { get; init; }
}

/// <summary>
/// Asks what an adapter config would make of a page.
/// </summary>
/// <remarks>
/// spec Bolum 5 step 4, without the wait. Adding a source means writing CSS
/// selectors against somebody else's markup, and the only way to find out
/// whether they were right used to be to switch the source on and see - which
/// either produces notices or produces nothing, and nothing is exactly what a
/// working source with a quiet week looks like.
/// </remarks>
public sealed record SourceCheckRequest
{
    /// <summary>The selectors to try.</summary>
    public required string AdapterConfig { get; init; }

    /// <summary>
    /// Test against this source's most recent archived page.
    /// </summary>
    /// <remarks>
    /// The archive rather than the network: it is the page the collector
    /// actually got, it costs the authority nothing, and it is what makes this
    /// answerable in a second. Ignored when <see cref="Html"/> is given.
    /// </remarks>
    public long? SourceId { get; init; }

    /// <summary>A page pasted in, for a source that has never been fetched.</summary>
    public string? Html { get; init; }

    /// <summary>The page's own address, so relative links resolve as they would.</summary>
    public string? Url { get; init; }
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
