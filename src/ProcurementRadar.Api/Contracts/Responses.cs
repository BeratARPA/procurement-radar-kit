namespace ProcurementRadar.Api.Contracts;

/// <summary>One notice as a proposed filter would have scored it.</summary>
public sealed record ScoredNotice
{
    public required long NoticeId { get; init; }

    public required string Title { get; init; }

    public required string AuthorityName { get; init; }

    public required int Score { get; init; }
}

/// <summary>
/// What a keyword list and a threshold would have done to the notices already
/// collected.
/// </summary>
/// <remarks>
/// Both halves are returned. What a filter catches is easy to be pleased with;
/// what it just misses is where the threshold is actually decided - at 30, a
/// maintenance contract for an ERP system scores 25 and is never mentioned.
/// </remarks>
public sealed record FilterPreviewResponse
{
    /// <summary>How many archived notices were scored.</summary>
    public required int Considered { get; init; }

    /// <summary>How many of them would have produced a notification.</summary>
    public required int WouldNotify { get; init; }

    public required int Threshold { get; init; }

    /// <summary>The highest scoring of those that pass.</summary>
    public required IReadOnlyList<ScoredNotice> Caught { get; init; }

    /// <summary>Those that scored something and still fell short.</summary>
    public required IReadOnlyList<ScoredNotice> Missed { get; init; }
}
