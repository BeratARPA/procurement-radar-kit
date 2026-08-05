namespace ProcurementRadar.Domain.Fetching;

/// <summary>
/// The only way anything in this codebase reaches the network. Enforces the
/// politeness rules on every caller's behalf: one request in flight per host, a
/// minimum gap between them, conditional requests, and robots.txt.
/// </summary>
public interface IHttpFetcher
{
    Task<FetchResult> FetchAsync(FetchRequest request, CancellationToken cancellationToken);
}
