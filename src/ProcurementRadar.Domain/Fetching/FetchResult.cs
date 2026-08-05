namespace ProcurementRadar.Domain.Fetching;

public enum FetchOutcome
{
    /// <summary>The page was downloaded and <see cref="FetchResult.Body"/> is set.</summary>
    Fetched,

    /// <summary>The server answered 304, so the body was deliberately not downloaded.</summary>
    NotModified,

    /// <summary>robots.txt forbids this path. Nothing was requested.</summary>
    SkippedByRobots,

    /// <summary>Every attempt failed. <see cref="FetchResult.HttpStatus"/> holds the last status, or 0.</summary>
    Failed
}

/// <summary>What to ask for. The conditional headers come from the previous
/// <c>raw_fetch</c> row for this source.</summary>
public sealed record FetchRequest(string Url, string? ETag = null, string? LastModified = null);

/// <summary>
/// The outcome of one fetch. Nothing here is interpreted: the collector does not
/// look inside the body, and does not even decide what encoding it is in.
/// </summary>
public sealed record FetchResult(
    FetchOutcome Outcome,
    int HttpStatus,
    byte[]? Body,
    string? ETag,
    string? LastModified,
    string? ContentHash,
    string? ContentType = null)
{
    public static FetchResult NotModified(string? etag, string? lastModified)
        => new(FetchOutcome.NotModified, 304, null, etag, lastModified, null);

    public static FetchResult SkippedByRobots()
        => new(FetchOutcome.SkippedByRobots, 0, null, null, null, null);

    public static FetchResult Failed(int httpStatus)
        => new(FetchOutcome.Failed, httpStatus, null, null, null, null);
}
