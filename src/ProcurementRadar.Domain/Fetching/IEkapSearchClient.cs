namespace ProcurementRadar.Domain.Fetching;

/// <summary>
/// One search to run on EKAP: which page to open, what to type into it, and
/// which of the app's own requests carries the answer.
/// </summary>
public sealed record EkapSearchRequest(
    string SearchUrl,
    string SearchText,
    string ApiUrlFragment,
    /// <summary>Clicked first where the page hides its search box behind something.</summary>
    string? PrepareClickSelector = null);

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
/// Keeping that behind an interface is what lets the EKAP adapters live in
/// Parsers, next to every other adapter, without Parsers ever referencing a
/// browser - and lets their tests run against a saved response.
/// </remarks>
public interface IEkapSearchClient
{
    Task<FetchResult> SearchAsync(EkapSearchRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The official announcement behind one tender, found by its registration
    /// number.
    /// </summary>
    /// <remarks>
    /// This is the only place that says what is actually being bought. A title
    /// reading "Yazılım ve Destek Hizmeti Alınacaktır" is compatible with an HR
    /// system, a licence renewal and a support contract; the announcement names
    /// it, and carries the table of quantities underneath.
    ///
    /// Reached the same way as everything else on EKAP - the application's own
    /// request, which it signs itself (D-12). There is no address to fetch
    /// instead: the announcement is drawn into a blank popup by javascript and
    /// has no url of its own.
    /// </remarks>
    Task<FetchResult> FetchAnnouncementAsync(string ikn, CancellationToken cancellationToken);
}
