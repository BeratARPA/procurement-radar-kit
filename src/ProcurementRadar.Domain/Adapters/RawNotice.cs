using ProcurementRadar.Domain.Enums;

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
    string? Ikn = null,
    // The hour was not on the page and had to be assumed. Carried through to the
    // notification rather than left in a log line, because the reader is the one
    // deciding whether there is still time.
    bool BidDeadlineTimeAssumed = false,
    // Which law this purchase is being run under, and which article of it. Only
    // EKAP states either; an authority's own listing page carries a title and a
    // link and nothing that says whether a 22/d purchase or an open tender is
    // behind it, so both stay null there rather than being guessed from the url.
    //
    // The distinction is what spec.md Bölüm 0 says the project is for: an open
    // tender and a purchase under the monetary limit are different jobs for a
    // three-person team working evenings.
    ProcurementType? ProcurementType = null,
    string? ProcedureCode = null)
{
    public IReadOnlyList<string> DocumentUrls { get; init; } = DocumentUrls ?? [];
}
