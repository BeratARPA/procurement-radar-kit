using Microsoft.Playwright;
using ProcurementRadar.Collector;
using Serilog;

namespace ProcurementRadar.Cli;

/// <summary>
/// Opens EKAP, runs a search, and reports what a result card actually exposes.
/// </summary>
/// <remarks>
/// spec.md section 13 calls EKAP the most fragile component, and the fragile half
/// is the browser interaction - the part no fixture can cover. This is the tool
/// for looking at it: it says what the buttons on a card are, where they lead,
/// and what happens when one is pressed, without changing anything.
///
/// It exists because that question could not be answered from the page's own
/// javascript. The result cards are invisible to document.querySelectorAll while
/// the accessibility tree lists them, so Playwright's role-based locators are the
/// only handle on them.
/// </remarks>
internal static class EkapProbeCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var url = ReadOption(args, "--url") ?? "https://ekapv2.kik.gov.tr/ekap/search";
        var searchText = ReadOption(args, "--search") ?? "test";

        // The site's own label, so it is data (CLAUDE.md rule 5) and lives in
        // config/ekap-ui.json beside the selector it goes with.
        var buttonName = ReadOption(args, "--button") ?? EkapUi.AnnouncementButtonName(null);

        using var playwright = await Playwright.CreateAsync().ConfigureAwait(false);

        await using var browser = await playwright.Chromium
            .LaunchAsync(new BrowserTypeLaunchOptions { Headless = true })
            .ConfigureAwait(false);

        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = "ProcurementRadar/1.0 (+mailto:" + (Environment.GetEnvironmentVariable("PR_CONTACT_EMAIL") ?? "") + ")",
            Locale = "tr-TR",
            TimezoneId = "Europe/Istanbul",
            AcceptDownloads = true
        }).ConfigureAwait(false);

        // Everything is watched before anything is clicked, because the thing we
        // are looking for may be a new tab, a download, or a request - and only
        // one of those leaves a trace on the page afterwards.
        var openedPages = new List<string>();
        var downloads = new List<string>();
        var requests = new List<string>();

        // The url is read after the popup settles, not in the handler: a new tab
        // is about:blank at the moment it is created and only then navigates.
        context.Page += async (_, page) =>
        {
            try
            {
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded,
                    new PageWaitForLoadStateOptions { Timeout = 30000 });
            }
            catch (Exception)
            {
                // A popup that never loads still tells us its url below.
            }

            openedPages.Add(page.Url);
        };

        var main = await context.NewPageAsync().ConfigureAwait(false);

        main.Download += (_, download) => downloads.Add($"{download.SuggestedFilename} <- {download.Url}");

        // The announcement itself comes back on one of these. Keeping the body is
        // the whole point: it is what says what is being bought.
        var apiBodies = new List<string>();

        main.Response += async (_, response) =>
        {
            if (!response.Url.Contains("/api/Ilan/", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                var body = await response.TextAsync();
                apiBodies.Add($"{response.Url} -> {body[..Math.Min(body.Length, 1400)]}");
            }
            catch (Exception)
            {
                apiBodies.Add($"{response.Url} -> (body could not be read)");
            }
        };
        main.Request += (_, request) =>
        {
            if (!request.ResourceType.Equals("image", StringComparison.Ordinal)
                && !request.ResourceType.Equals("font", StringComparison.Ordinal)
                && !request.ResourceType.Equals("stylesheet", StringComparison.Ordinal))
            {
                requests.Add($"{request.Method} {request.Url}");
            }
        };

        await main.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        }).ConfigureAwait(false);

        var input = main.Locator("input[placeholder='Ara']:visible").First;
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 }).ConfigureAwait(false);
        await input.FillAsync(searchText).ConfigureAwait(false);
        await input.PressAsync("Enter").ConfigureAwait(false);

        var button = main.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = buttonName }).First;

        try
        {
            await button.WaitForAsync(new LocatorWaitForOptions { Timeout = 45000 }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Log.Error("No '{Button}' appeared after searching for '{SearchText}'", buttonName, searchText);
            return 1;
        }

        // What the element is, before touching it. If it turns out to be an
        // anchor, its href is the answer and nothing needs to be clicked at all.
        var described = await button.EvaluateAsync<string>(
            """
            element => {
              const anchor = element.closest('a') || element.querySelector('a');
              return JSON.stringify({
                tag: element.tagName,
                cls: (element.className || '').toString().slice(0, 120),
                href: anchor ? anchor.getAttribute('href') : null,
                target: anchor ? anchor.getAttribute('target') : null,
                onclick: element.getAttribute('onclick'),
                html: element.outerHTML.slice(0, 400)
              });
            }
            """).ConfigureAwait(false);

        Log.Information("The '{Button}' element is {Described}", buttonName, described);

        var requestsBefore = requests.Count;

        await button.ClickAsync(new LocatorClickOptions { Timeout = 30000 }).ConfigureAwait(false);

        // Give a popup, a download or a request time to appear.
        await main.WaitForTimeoutAsync(12000).ConfigureAwait(false);

        Log.Information("After the click: {Pages} new page(s), {Downloads} download(s)",
            openedPages.Count, downloads.Count);

        foreach (var page in openedPages)
        {
            Log.Information("  new page: {Url}", page);
        }

        foreach (var download in downloads)
        {
            Log.Information("  download: {Download}", download);
        }

        foreach (var request in requests.Skip(requestsBefore).Take(25))
        {
            Log.Information("  request: {Request}", request);
        }

        foreach (var body in apiBodies)
        {
            Log.Information("  ilan api: {Body}", body);
        }

        Log.Information("Main page is now at {Url}", main.Url);

        return 0;
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
