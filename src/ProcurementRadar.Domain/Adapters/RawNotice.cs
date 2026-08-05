namespace ProcurementRadar.Domain.Adapters;

/// <summary>
/// One notice as an adapter read it off a page, before normalisation and
/// deduplication (T-09).
/// </summary>
/// <remarks>
/// <see cref="Title"/> and <see cref="SourceUrl"/> are mandatory: a row missing
/// either is skipped rather than stored. <see cref="BidDeadlineAt"/> is
/// best-effort but is the field the whole product turns on, so a null one is
/// still emitted and flagged downstream rather than dropped.
/// </remarks>
public sealed record RawNotice(
    string Title,
    string SourceUrl,
    string? AuthorityName = null,
    string? Description = null,
    DateTimeOffset? BidDeadlineAt = null,
    decimal? EstimatedCost = null,
    IReadOnlyList<string>? DocumentUrls = null,
    // Not in spec.md section 6, but deduplication turns on it: where a page
    // carries the state's own tender number, that number is the identity and
    // nothing computed can beat it (spec Bölüm 8). Only EKAP pages have one.
    string? Ikn = null)
{
    public IReadOnlyList<string> DocumentUrls { get; init; } = DocumentUrls ?? [];
}
