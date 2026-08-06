using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ProcurementRadar.Domain.Fetching;

namespace ProcurementRadar.Collector;

/// <summary>
/// Runs an EKAP search in a real browser and keeps the json their own api
/// answered with.
/// </summary>
/// <remarks>
/// The browser is not here to render anything. Nothing is read off the page: the
/// only thing taken is the body of the request the application makes for itself,
/// which it signs with headers we deliberately do not reproduce (D-12). A
/// screenshot would be useless to us and the rendered table is a worse copy of
/// the json behind it.
///
/// Everything it touches goes through the same politeness gate as the plain http
/// fetcher, so EKAP queues behind the same one-request-at-a-time, five-seconds-
/// apart rule as every municipality.
/// </remarks>
public sealed class PlaywrightEkapSearchClient : IEkapSearchClient, IAsyncDisposable
{
    /// <summary>The single visible search box. Verified to be unambiguous.</summary>
    private const string SearchInputSelector = "input[placeholder='Ara']:visible";

    private readonly HostPolitenessGate _gate;
    private readonly HttpFetcherOptions _options;
    private readonly EkapBrowserOptions _browserOptions;
    private readonly ILogger<PlaywrightEkapSearchClient> _logger;
    private readonly SemaphoreSlim _startup = new(1, 1);

    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightEkapSearchClient(
        HostPolitenessGate gate,
        HttpFetcherOptions options,
        EkapBrowserOptions browserOptions,
        ILogger<PlaywrightEkapSearchClient> logger)
    {
        _gate = gate;
        _options = options;
        _browserOptions = browserOptions;
        _logger = logger;
    }

    public async Task<FetchResult> SearchAsync(
        EkapSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.SearchUrl, UriKind.Absolute, out var uri))
        {
            _logger.LogError("{Url} is not an absolute url", request.SearchUrl);
            return FetchResult.Failed(0);
        }

        return await _gate
            .RunAsync(uri.Host, token => AttemptAsync(request, token), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the search, and runs it once more if the application did not finish
    /// starting in time.
    /// </summary>
    /// <remarks>
    /// Measured, not assumed: the same page loaded in twenty seconds on one run
    /// and had not produced its search box in sixty on the next. EKAP assembles
    /// itself from separately loaded modules, so a slow one delays everything
    /// behind it, and the difference between the two runs was the site's mood
    /// rather than anything here.
    ///
    /// One retry only, and inside the politeness gate, so the second attempt
    /// still waits its turn. A source that is genuinely down should be recorded
    /// as failing rather than hammered - that is what T-15 counts.
    /// </remarks>
    private async Task<FetchResult> AttemptAsync(
        EkapSearchRequest request,
        CancellationToken cancellationToken)
    {
        var first = await RunSearchAsync(request, cancellationToken).ConfigureAwait(false);

        if (first.Outcome == FetchOutcome.Fetched)
        {
            return first;
        }

        _logger.LogWarning(
            "First EKAP attempt for '{SearchText}' did not produce a result; trying once more",
            request.SearchText);

        return await RunSearchAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FetchResult> RunSearchAsync(
        EkapSearchRequest request,
        CancellationToken cancellationToken)
    {
        IBrowserContext? context = null;

        try
        {
            var browser = await BrowserAsync(cancellationToken).ConfigureAwait(false);

            context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                // The same User-Agent as everywhere else, so an administrator
                // reading their logs sees one project and one address to reach us
                // at, not a browser pretending to be a person.
                UserAgent = _options.UserAgent,
                Locale = "tr-TR",
                TimezoneId = "Europe/Istanbul"
            }).ConfigureAwait(false);

            var page = await context.NewPageAsync().ConfigureAwait(false);

            await page.GotoAsync(request.SearchUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = (float)_browserOptions.PageTimeout.TotalMilliseconds
            }).ConfigureAwait(false);

            var input = page.Locator(SearchInputSelector).First;

            if (request.PrepareClickSelector is { Length: > 0 } prepare
                && !await input.IsVisibleAsync().ConfigureAwait(false))
            {
                // The doğrudan temin page opens on a notice about the pilot and
                // keeps its search box behind a button. Only clicked when the box
                // is not already there, so the day EKAP drops the notice this
                // costs nothing and nothing breaks.
                _logger.LogInformation("No search box yet; clicking {Selector} first", prepare);

                await page.Locator(prepare).First
                    .ClickAsync(new LocatorClickOptions
                    {
                        Timeout = (float)_browserOptions.PageTimeout.TotalMilliseconds
                    })
                    .ConfigureAwait(false);
            }

            await input.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = (float)_browserOptions.PageTimeout.TotalMilliseconds
            }).ConfigureAwait(false);

            // Armed before the search is triggered, or the response can arrive
            // while we are still setting up the wait.
            var waitingForResults = page.WaitForResponseAsync(
                response => response.Url.Contains(request.ApiUrlFragment, StringComparison.Ordinal)
                    && response.Status == 200,
                new PageWaitForResponseOptions { Timeout = (float)_browserOptions.ResultTimeout.TotalMilliseconds });

            await input.FillAsync(request.SearchText).ConfigureAwait(false);

            // Enter rather than the button beside it. The button has no stable
            // handle of its own and the site's component library renames its
            // classes between releases; a key press does not go stale.
            await input.PressAsync("Enter").ConfigureAwait(false);

            var result = await waitingForResults.ConfigureAwait(false);
            var body = await result.BodyAsync().ConfigureAwait(false);

            _logger.LogInformation(
                "EKAP answered {Url} with {ByteCount} bytes for '{SearchText}'",
                request.ApiUrlFragment,
                body.Length,
                request.SearchText);

            return new FetchResult(
                FetchOutcome.Fetched,
                result.Status,
                body,
                ETag: null,
                LastModified: null,
                ContentHash: Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant(),
                ContentType: "application/json; charset=utf-8");
        }
        catch (TimeoutException exception)
        {
            // Either the page never finished booting or the search never
            // answered. Both are ordinary failures for T-15 to count, not a
            // reason to bring the run down.
            _logger.LogError(
                exception,
                "EKAP search for '{SearchText}' timed out at {Url}",
                request.SearchText,
                request.SearchUrl);

            return FetchResult.Failed(0);
        }
        catch (PlaywrightException exception)
        {
            _logger.LogError(
                exception,
                "EKAP search for '{SearchText}' failed in the browser",
                request.SearchText);

            return FetchResult.Failed(0);
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
    /// One browser for the life of the worker, started on first use.
    /// </summary>
    /// <remarks>
    /// Launching Chromium costs a second or so and a few hundred megabytes. On
    /// the machine D-10 describes - somebody's old laptop - starting one per poll
    /// would be the most expensive thing the worker does. Each search still gets
    /// its own context, so nothing is shared between them but the process.
    /// </remarks>
    private async Task<IBrowser> BrowserAsync(CancellationToken cancellationToken)
    {
        if (_browser is { IsConnected: true } running)
        {
            return running;
        }

        await _startup.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_browser is { IsConnected: true } started)
            {
                return started;
            }

            _playwright ??= await Playwright.CreateAsync().ConfigureAwait(false);

            _logger.LogInformation("Starting a browser for EKAP");

            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = _browserOptions.Headless
            }).ConfigureAwait(false);

            return _browser;
        }
        finally
        {
            _startup.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync().ConfigureAwait(false);
        }

        _playwright?.Dispose();
        _startup.Dispose();
    }
}

/// <summary>How the EKAP browser is run.</summary>
public sealed record EkapBrowserOptions
{
    public bool Headless { get; init; } = true;

    /// <summary>How long the application may take to boot before giving up.</summary>
    public TimeSpan PageTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How long the search itself may take to answer.</summary>
    public TimeSpan ResultTimeout { get; init; } = TimeSpan.FromSeconds(45);
}
