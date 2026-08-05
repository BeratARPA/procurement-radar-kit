namespace ProcurementRadar.Domain.Entities;

/// <summary>
/// One record per fetch. Table <c>raw_fetch</c>. Rows are never deleted: when a
/// parser breaks we fix it and reprocess the archive.
/// </summary>
public sealed record RawFetch
{
    public long Id { get; init; }

    public required long SourceId { get; init; }

    public required DateTimeOffset FetchedAt { get; init; }

    public int? HttpStatus { get; init; }

    public string? ETag { get; init; }

    public string? LastModified { get; init; }

    /// <summary>sha256 of the body, used for change detection.</summary>
    public required string ContentHash { get; init; }

    /// <summary>Path of the gzipped body under PR_DATA_DIR (D-04).</summary>
    public required string BodyPath { get; init; }

    public string? ParserVersion { get; init; }

    public bool IsProcessed { get; init; }
}
