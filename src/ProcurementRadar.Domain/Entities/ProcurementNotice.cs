using ProcurementRadar.Domain.Enums;

namespace ProcurementRadar.Domain.Entities;

/// <summary>A normalized notice. Table <c>procurement_notice</c>.</summary>
public sealed record ProcurementNotice
{
    public long Id { get; init; }

    public required long SourceId { get; init; }

    public required long RawFetchId { get; init; }

    /// <summary>IKN when the page carries one, otherwise a computed hash (T-09).</summary>
    public required string ExternalKey { get; init; }

    /// <summary>Turkish tender registration number.</summary>
    public string? Ikn { get; init; }

    public required string AuthorityName { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public ProcurementType? ProcurementType { get; init; }

    /// <summary>
    /// Kept as free text rather than an enum. The article system behind it
    /// (art22a..art22f, art21a..art21f, open, restricted and more) is wider than
    /// the sample in the spec, and a closed enum would reject valid notices.
    /// </summary>
    public string? ProcedureCode { get; init; }

    /// <summary>Turkish equivalent of CPV codes.</summary>
    public string[]? OkasCodes { get; init; }

    public decimal? EstimatedCost { get; init; }

    public string Currency { get; init; } = "TRY";

    /// <summary>The most critical field in the product.</summary>
    public DateTimeOffset? BidDeadlineAt { get; init; }

    public DateTimeOffset? OpeningAt { get; init; }

    public required string SourceUrl { get; init; }

    /// <summary>
    /// Set by the repository on first insert and never recomputed. Whatever a
    /// caller puts here is ignored on the way in.
    /// </summary>
    public DateTimeOffset FirstSeenAt { get; init; }

    /// <summary>Set by the repository on every upsert.</summary>
    public DateTimeOffset LastSeenAt { get; init; }

    public NoticeStatus Status { get; init; } = NoticeStatus.New;
}
