namespace ProcurementRadar.Domain.Entities;

/// <summary>Scoring result for one notice. Table <c>notice_score</c>.</summary>
public sealed record NoticeScore
{
    public required long NoticeId { get; init; }

    public int? RuleScore { get; init; }

    /// <summary>Stays null in v1: there is no LLM in this version (D-03).</summary>
    public int? LlmScore { get; init; }

    public string? Rationale { get; init; }

    public int? EstimatedManDays { get; init; }

    public string? RiskNote { get; init; }

    public string? Model { get; init; }

    public DateTimeOffset? ScoredAt { get; init; }
}
