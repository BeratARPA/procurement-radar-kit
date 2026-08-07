using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ProcurementRadar.Domain.Fetching;

namespace ProcurementRadar.Collector;

/// <summary>
/// Loads a page in Chromium and returns the html its scripts produced.
/// </summary>
/// <remarks>
/// For the sources whose listings are not in the html the server sends. It was
/// worth measuring before writing: seven pages, all of which certainly carry
/// tenders, and try-source could find nothing on any of them because there is
/// nothing there to find until a script runs.
///
/// Everything else is unchanged. The rendered html is archived like any other
/// body, and the parser reads it as text without learning that a browser was
/// involved. The politeness gate is the same one the EKAP browser uses, so a
/// rendered source queues behind every other request to its host rather than
/// being an exception to the rules.
/// </remarks>
public sealed class PlaywrightPageRenderer : IPageRenderer, IAsyncDisposable
{
    private readonly HostPolitenessGate _gate;
    private readonly HttpFetcherOptions _options;
    private readonly EkapBrowserOptions _browserOptions;
    private readonly ILogger<PlaywrightPageRenderer> _logger;
    private readonly SemaphoreSlim _browserLock = new(1, 1);

    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightPageRenderer(
        HostPolitenessGate gate,
        HttpFetcherOptions options,
        EkapBrowserOptions browserOptions,
        ILogger<PlaywrightPageRenderer> logger)
    {
        _gate = gate;
        _options = options;
        _browserOptions = browserOptions;
        _logger = logger;
    }

    public async Task<FetchResult> RenderAsync(string url, CancellationToken cancellationToken)
        => await _gate
            .RunAsync(
                new Uri(url).Host,
                token => RenderOnceAsync(url, token),
                cancellationToken)
            .ConfigureAwait(false);

    private async Task<FetchResult> RenderOnceAsync(string url, CancellationToken cancellationToken)
    {
        IBrowserContext? context = null;

        try
        {
            var browser = await BrowserAsync(cancellationToken).ConfigureAwait(false);

            context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                // The same User-Agent as everywhere else, so an administrator
                // reading their logs sees one project and one address to reach
                // us at, not a browser pretending to be a person.
                UserAgent = _options.UserAgent,
                Locale = "tr-TR",
                TimezoneId = "Europe/Istanbul"
            }).ConfigureAwait(false);

            var page = await context.NewPageAsync().ConfigureAwait(false);

            await page.GotoAsync(url, new PageGotoOptions
            {
                // Not NetworkIdle: a page with a chat widget or an analytics
                // beacon never goes idle, and waiting for that is how a fetch
                // becomes a timeout on a page that finished loading.
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = (float)_browserOptions.PageTimeout.TotalMilliseconds
            }).ConfigureAwait(false);

            var html = await SettledAsync(page).ConfigureAwait(false);
            var bytes = Encoding.UTF8.GetBytes(html);

            _logger.LogInformation("Rendered {Url}: {ByteCount} bytes after scripts", url, bytes.Length);

            return new FetchResult(
                FetchOutcome.Fetched,
                200,
                bytes,
                ETag: null,
                LastModified: null,
                // Over the rendered bytes, so change detection means "the list
                // changed" rather than "a script tag moved".
                ContentHash: Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                ContentType: "text/html; charset=utf-8");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Could not render {Url}", url);

            return new FetchResult(FetchOutcome.Failed, 0, null, null, null, string.Empty, null);
        }
        finally
        {
            if (context is not null)
            {
                await context.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// The html once the page has stopped changing.
    /// </summary>
    /// <remarks>
    /// A fixed pause is a guess at somebody else's application. Two and a half
    /// seconds was enough for a page that renders its list from data it already
    /// has, and not enough for an Angular application that asks a server for the
    /// list after it has drawn its shell - which is what ilan.gov.tr does, and
    /// what it looked like when it came back holding its own category labels.
    ///
    /// So the page is sampled instead: when the html stops growing between two
    /// looks, it is done. Bounded, because a page with a carousel or a clock in
    /// it never truly settles.
    /// </remarks>
    private static async Task<string> SettledAsync(IPage page)
    {
        var previous = string.Empty;

        for (var attempt = 0; attempt < 8; attempt++)
        {
            await page.WaitForTimeoutAsync(1200).ConfigureAwait(false);

            var html = await page.ContentAsync().ConfigureAwait(false);

            // Compared by length rather than by content: a timestamp or a
            // rotating banner changes the html on every look without changing
            // anything worth waiting for.
            if (html.Length == previous.Length && attempt > 0)
            {
                return html;
            }

            previous = html;
        }

        return previous;
    }

    /// <summary>
    /// One browser for the process, started the first time it is needed.
    /// </summary>
    /// <remarks>
    /// Starting Chromium costs a second or two and a few hundred megabytes on
    /// the machine D-10 describes - somebody's laptop - so it is started once
    /// and kept, and never started at all on a run that renders nothing.
    /// </remarks>
    private async Task<IBrowser> BrowserAsync(CancellationToken cancellationToken)
    {
        if (_browser is { IsConnected: true })
        {
            return _browser;
        }

        await _browserLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_browser is { IsConnected: true })
            {
                return _browser;
            }

            _playwright ??= await Playwright.CreateAsync().ConfigureAwait(false);

            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = _browserOptions.Headless
            }).ConfigureAwait(false);

            return _browser;
        }
        finally
        {
            _browserLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync().ConfigureAwait(false);
        }

        _playwright?.Dispose();
        _browserLock.Dispose();
    }
}
