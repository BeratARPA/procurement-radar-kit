namespace ProcurementRadar.Domain.Entities;

/// <summary>Who won a notice and for how much. Table <c>award</c>.</summary>
public sealed record Award
{
    public required long NoticeId { get; init; }

    public string? WinnerName { get; init; }

    public decimal? ContractAmount { get; init; }

    public int? BidCount { get; init; }

    public DateTimeOffset? AwardedAt { get; init; }
}
