namespace ProcurementRadar.Domain.Fetching;

/// <summary>
/// Fetches a page the way a browser would, and returns the html after its
/// scripts have run.
/// </summary>
/// <remarks>
/// spec section 13 put Playwright in the stack for "about 10% of sources, only
/// where it is needed", and this is what needed it. Seven pages that certainly
/// carry tenders - Bursa's direct procurement list, Antalya's, Yenimahalle,
/// Ankara Üniversitesi, Başkent Doğalgaz, Ankara MEB - hold nothing at all in
/// the html the server sends. Their lists are assembled in the browser.
///
/// This is a fetching concern and nothing else. The body that comes back is
/// archived like any other (D-11) and read by the same parser, which stays a
/// pure function over text and never learns that a browser was involved.
///
/// It is expensive - a browser tab against a few kilobytes of html - so it is
/// asked for per source rather than used by default.
/// </remarks>
public interface IPageRenderer
{
    Task<FetchResult> RenderAsync(string url, CancellationToken cancellationToken);
}
