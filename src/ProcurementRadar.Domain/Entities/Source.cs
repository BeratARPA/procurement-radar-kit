using ProcurementRadar.Domain.Enums;

namespace ProcurementRadar.Domain.Entities;

/// <summary>A single page that gets polled. Table <c>source</c>.</summary>
public sealed record Source
{
    public long Id { get; init; }

    public required string AuthorityName { get; init; }

    public required AuthorityType AuthorityType { get; init; }

    public required string Province { get; init; }

    public string? District { get; init; }

    public required string Url { get; init; }

    /// <summary>Which parser class handles this page.</summary>
    public required string AdapterKey { get; init; }

    /// <summary>
    /// Raw jsonb, kept as text here. The adapter that owns the key is the only
    /// thing that knows its shape, so the domain does not try to model it.
    /// </summary>
    public string? AdapterConfig { get; init; }

    public int PollIntervalMinutes { get; init; } = 15;

    public bool IsActive { get; init; } = true;

    public DateTimeOffset? RobotsCheckedAt { get; init; }

    public DateTimeOffset? LastFetchAt { get; init; }

    public DateTimeOffset? LastSuccessAt { get; init; }

    public DateTimeOffset? LastNewNoticeAt { get; init; }

    public int ConsecutiveFailures { get; init; }

    public string? Notes { get; init; }
}
