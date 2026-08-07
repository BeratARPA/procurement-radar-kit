using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ProcurementRadar.Collector;
using Serilog;
using Serilog.Extensions.Logging;

namespace ProcurementRadar.Cli;

/// <summary>
/// Opens a page in the browser and reports what it asked the server for.
/// </summary>
/// <remarks>
/// The tool that was missing. Two sources are single-page applications whose
/// listings never appear in the html - Yenimahalle returns a 1,716 byte React
/// shell with the word "ihale" nowhere in it - and rendering does not help,
/// because whatever the application fetches is not arriving or not being drawn
/// in a headless browser.
///
/// The answer, in both cases, is the one that worked for EKAP: stop trying to
/// read the page and read what the page reads. This lists every request the
/// application makes, so the endpoint behind a listing can be found in a minute
/// instead of guessed at across an afternoon.
///
/// It only watches. Nothing is stored and nothing is added; what to do with the
/// address it finds is a separate decision.
/// </remarks>
internal static class WatchRequestsCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var url = ReadOption(args, "--url");

        if (string.IsNullOrWhiteSpace(url))
        {
            Log.Error("Usage: watch-requests --url <url> [--contains <text>]");
            return 1;
        }

        // Narrows a chatty page to the requests worth reading. Without it every
        // font and analytics beacon is listed too.
        var contains = ReadOption(args, "--contains");

        using var loggerFactory = new SerilogLoggerFactory(Log.Logger);
        var options = new HttpFetcherOptions
        {
            ContactEmail = Environment.GetEnvironmentVariable("PR_CONTACT_EMAIL") ?? string.Empty
        };

        using var httpClient = new HttpClient();
        var robots = new RobotsGuard(
            httpClient,
            new HostPolitenessGate(TimeProvider.System, options),
            TimeProvider.System,
            options,
            loggerFactory.CreateLogger("watch-requests"));

        if (!await robots.IsAllowedAsync(new Uri(url), cancellationToken).ConfigureAwait(false))
        {
            // Same rule as everywhere else. A diagnostic is still a request.
            Log.Warning("robots.txt disallows {Url}", url);
            return 1;
        }

        using var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        await using var browser = await playwright.Chromium
            .LaunchAsync(new BrowserTypeLaunchOptions { Headless = true })
            .ConfigureAwait(false);

        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = options.UserAgent,
            Locale = "tr-TR",
            TimezoneId = "Europe/Istanbul"
        }).ConfigureAwait(false);

        var page = await context.NewPageAsync().ConfigureAwait(false);
        var seen = new List<string>();

        page.Response += (_, response) =>
        {
            var address = response.Url;

            // Static assets are never the answer and drown everything else.
            if (address.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                || address.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
                || address.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase)
                || address.Contains("/logo", StringComparison.OrdinalIgnoreCase)
                || address.Contains("googletagmanager", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (contains is { Length: > 0 } && !address.Contains(contains, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            seen.Add($"{response.Status} {response.Request.Method} {address}");
        };

        page.RequestFailed += (_, request) => seen.Add($"FAILED {request.Method} {request.Url} - {request.Failure}");

        await page.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        }).ConfigureAwait(false);

        // Long enough for an application to draw its shell, ask for its data and
        // be refused if it is going to be.
        await page.WaitForTimeoutAsync(12000).ConfigureAwait(false);

        Log.Information("{Count} request(s) worth reading at {Url}:", seen.Count, url);

        foreach (var entry in seen.Distinct(StringComparer.Ordinal))
        {
            Log.Information("  {Entry}", entry);
        }

        var html = await page.ContentAsync().ConfigureAwait(false);

        Log.Information("Document ended at {Length} characters", html.Length);

        return 0;
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
