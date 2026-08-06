namespace ProcurementRadar.Domain.Fetching;

/// <summary>
/// Runs one search on EKAP and hands back the json its own api returned.
/// </summary>
/// <remarks>
/// A port rather than a class because of what the implementation has to be. EKAP
/// serves its results from an api that refuses unsigned requests: the Angular app
/// adds four headers carrying an encrypted timestamp and request id, and without
/// them the answer is 401. Reproducing that signature would mean lifting the key
/// out of their bundle and impersonating their client, which is circumventing an
/// access control, so the only implementation is a real browser running their own
/// javascript (D-12).
///
/// Keeping that behind an interface is what lets <c>EkapAdapter</c> live in
/// Parsers, next to every other adapter, without Parsers ever referencing a
/// browser - and lets the adapter's tests run against a saved response.
/// </remarks>
public interface IEkapSearchClient
{
    /// <summary>
    /// Searches <paramref name="searchUrl"/> for <paramref name="searchText"/>
    /// and returns the raw body of the api response behind it.
    /// </summary>
    Task<FetchResult> SearchAsync(
        string searchUrl,
        string searchText,
        CancellationToken cancellationToken);
}
