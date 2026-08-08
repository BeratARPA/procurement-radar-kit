using System.Text.Json;
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
    /// <summary>The body of the most recent search response, for --paging.</summary>
    private static string? _lastSearchBody;

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var url = ReadOption(args, "--url") ?? "https://ekapv2.kik.gov.tr/ekap/search";
        var searchText = ReadOption(args, "--search") ?? "test";

        // The site's own label, so it is data (CLAUDE.md rule 5) and lives in
        // config/ekap-ui.json beside the selector it goes with.
        var buttonName = ReadOption(args, "--button") ?? EkapUi.AnnouncementButtonName(null);

        // Where to keep the whole response. The log truncates, and the thing you
        // usually need is the part past the truncation.
        var dumpPath = ReadOption(args, "--dump");

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

        // The search response itself, kept so --paging can report what a filter
        // did to the result set rather than what the screen suggests it did.
        main.Response += async (_, response) =>
        {
            if (!response.Url.Contains("GetListByParameters", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                _lastSearchBody = await response.TextAsync();
            }
            catch (Exception)
            {
                // A body that cannot be read tells us nothing and breaks nothing.
            }
        };

        main.Response += async (_, response) =>
        {
            if (!response.Url.Contains("/api/Ilan/", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                var body = await response.TextAsync();

                if (dumpPath is { Length: > 0 })
                {
                    await File.WriteAllTextAsync(dumpPath, body, cancellationToken);
                }

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

        // The doğrudan temin page keeps its search box behind the pilot notice,
        // so a probe without this reads that notice and reports no search box -
        // which is true and useless.
        if (ReadOption(args, "--prepare") is { Length: > 0 } prepare)
        {
            try
            {
                await main.Locator(prepare).First
                    .ClickAsync(new LocatorClickOptions { Timeout = 30000 })
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                Log.Warning("Nothing matched {Selector}; carrying on in case the box is already there", prepare);
            }
        }

        try
        {
            await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // "I did not find the search box" is half an answer. The half that
            // matters is what was on the page instead, and a probe that makes you
            // reproduce it by hand to find out is not doing its job.
            var instead = await main.InnerTextAsync("body").ConfigureAwait(false);

            Log.Error(
                "No search box at {Url} after 60s. The page said: {Text}",
                url,
                instead.ReplaceLineEndings(" ").Trim() is { Length: > 0 } text
                    ? text[..Math.Min(text.Length, 400)]
                    : "(nothing)");

            return 1;
        }
        await input.FillAsync(searchText).ConfigureAwait(false);
        await input.PressAsync("Enter").ConfigureAwait(false);

        // --paging answers one question and then stops: how does this page let a
        // person see result eleven? EKAP reports thousands of matches and hands
        // back ten of them, sorted by tender date with the furthest future first,
        // so the notices closing this week - the ones worth knowing about - are
        // the ones underneath. Whatever moves to the next page is the only way to
        // reach them, and it has to be measured rather than guessed at, because
        // this is somebody else's Angular application.
        if (args.Contains("--paging", StringComparer.Ordinal))
        {
            return await ReportPagingAsync(main, ReadOption(args, "--filter"), searchText, cancellationToken)
                .ConfigureAwait(false);
        }

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

    /// <summary>
    /// Prints whatever this page offers for reaching the results past the tenth.
    /// </summary>
    /// <remarks>
    /// Reports rather than decides. A paginator, a page-size selector and a
    /// sortable date column are three different answers with three different
    /// costs, and which one EKAP has is a fact about their application, not a
    /// preference of ours.
    /// </remarks>
    private static async Task<int> ReportPagingAsync(
        IPage page,
        string? filterLabel,
        string searchText,
        CancellationToken cancellationToken)
    {
        // Long enough for the results and their controls to be drawn. The search
        // response itself arrives well inside this.
        await page.WaitForTimeoutAsync(9000).ConfigureAwait(false);

        // --filter measures the alternative to paging. EKAP's own panel offers
        // "İlan Tarihi: Bugün", which asks a different and much better question
        // than "give me ten of nine thousand": everything announced today. If
        // that filter reaches the api, one page is the whole answer and the 943
        // pages underneath stop mattering.
        if (filterLabel is { Length: > 0 })
        {
            var before = await CountTextAsync(page).ConfigureAwait(false);

            var control = page.GetByText(filterLabel, new PageGetByTextOptions { Exact = true }).First;

            await control.ClickAsync(new LocatorClickOptions { Timeout = 20000 }).ConfigureAwait(false);
            await page.WaitForTimeoutAsync(1500).ConfigureAwait(false);

            // Re-run the search so the filter reaches the api rather than only
            // the screen.
            var input = page.Locator("input[placeholder='Ara']:visible").First;
            await input.FillAsync(searchText).ConfigureAwait(false);
            await input.PressAsync("Enter").ConfigureAwait(false);
            await page.WaitForTimeoutAsync(9000).ConfigureAwait(false);

            Log.Information(
                "With '{Filter}': {Before} -> {After}. Api says {Api}",
                filterLabel,
                before,
                await CountTextAsync(page).ConfigureAwait(false),
                DescribeSearchBody());

            // Exactly what the client now does, so a failure here is a failure
            // there. The client reported it could not ask for fifty and this is
            // where that gets settled rather than guessed at.
            var box = page.Locator(".paginator-container .page-box").First;

            Log.Information(
                "Page-size box visible: {Visible}",
                await box.IsVisibleAsync().ConfigureAwait(false));

            if (await box.IsVisibleAsync().ConfigureAwait(false))
            {
                await box.ClickAsync(new LocatorClickOptions { Timeout = 12000 }).ConfigureAwait(false);
                await page.WaitForTimeoutAsync(1200).ConfigureAwait(false);

                var items = await page.EvaluateAsync<string>(
                    """
                    () => {
                      const numeric = [...document.querySelectorAll('*')]
                        .filter(e => e.children.length === 0 && /^\s*\d{1,3}\s*$/.test(e.textContent || ''))
                        .map(e => {
                          const path = [];
                          for (let p = e; p && path.length < 4; p = p.parentElement) {
                            path.push(p.tagName.toLowerCase()
                              + (p.className ? '.' + p.className.toString().trim().split(/\s+/).slice(0, 3).join('.') : ''));
                          }
                          return {
                            text: (e.textContent || '').trim(),
                            visible: !!e.offsetParent,
                            path: path.join(' < ').slice(0, 200)
                          };
                        })
                        .filter(x => ['10', '20', '50'].includes(x.text));
                      return JSON.stringify(numeric.slice(0, 8), null, 1);
                    }
                    """).ConfigureAwait(false);

                Log.Information("Options as they really are: {Items}", items);

                var option = page.Locator(".dx-list-item-content:text-is('50')").First;

                Log.Information(
                    "The '50' option is {State}",
                    await option.CountAsync().ConfigureAwait(false) > 0 ? "there" : "missing");

                try
                {
                    await option.ClickAsync(new LocatorClickOptions { Timeout = 12000 }).ConfigureAwait(false);
                    await page.WaitForTimeoutAsync(8000).ConfigureAwait(false);

                    Log.Information("After asking for 50: {Api}", DescribeSearchBody());
                }
                catch (Exception exception) when (exception is TimeoutException or PlaywrightException)
                {
                    Log.Warning("Clicking '50' failed: {Message}", exception.Message[..Math.Min(exception.Message.Length, 120)]);
                }
            }
        }
        else
        {
            Log.Information("Without a filter the api says {Api}", DescribeSearchBody());
        }

        var found = await page.EvaluateAsync<string>(
            """
            () => {
              const seen = [];
              const add = (why, element) => {
                if (!element || seen.some(s => s.html === element.outerHTML.slice(0, 300))) return;
                seen.push({
                  why,
                  tag: element.tagName.toLowerCase(),
                  cls: (element.className || '').toString().slice(0, 100),
                  text: (element.innerText || '').replace(/\s+/g, ' ').trim().slice(0, 90),
                  html: element.outerHTML.slice(0, 300)
                });
              };

              for (const selector of [
                // DevExtreme first: that is what EKAP is built from, and its
                // pager carries a page-size selector - which is worth more than
                // a next-page button, because one larger request beats five.
                '.dx-pager', '.dx-pages', '.dx-page-sizes', '.dx-page-size',
                '.dx-navigate-button', '.dx-info',
                'p-paginator', '.p-paginator', 'ngb-pagination', 'mat-paginator',
                'ul.pagination', '[class*=paginator]', '[class*=pagination]',
                'p-dropdown', 'select'
              ]) {
                document.querySelectorAll(selector).forEach(e => add(selector, e));
              }

              // Sortable column headers: the other way to move the window, and
              // the cheaper one if the date column can be turned around.
              document.querySelectorAll('th').forEach(e => {
                if (e.getAttribute('ng-reflect-sortable') !== null
                    || (e.className || '').toString().includes('sort')
                    || e.querySelector('[class*=sort]')) {
                  add('th[sortable]', e);
                }
              });

              // Nothing matched the shapes we know, so describe what is actually
              // there: every control with a short label, which is what a
              // paginator looks like whatever it is built from.
              if (seen.length === 0) {
                document.querySelectorAll('button, a, [role=button], li').forEach(e => {
                  const t = (e.innerText || '').replace(/\s+/g, ' ').trim();
                  if (t.length > 0 && t.length <= 24) add('label:' + t, e);
                });
              }

              return JSON.stringify(seen.slice(0, 40), null, 1);
            }
            """).ConfigureAwait(false);

        Log.Information("Paging controls on the results page: {Found}", found);

        var shape = await page.EvaluateAsync<string>(
            """
            () => JSON.stringify({
              scrollHeight: document.body.scrollHeight,
              rows: document.querySelectorAll('tr').length,
              // DevExtreme draws its grid out of divs, so counting <tr> reports
              // zero on a page that is showing ten results perfectly well.
              dxDataRows: document.querySelectorAll('.dx-data-row').length,
              dxGrids: document.querySelectorAll('dx-data-grid, .dx-datagrid').length,
              dxPagers: document.querySelectorAll('.dx-pager').length,
              cards: document.querySelectorAll('[class*=card]').length,
              tables: document.querySelectorAll('table').length,
              buttons: document.querySelectorAll('button').length
            })
            """).ConfigureAwait(false);

        Log.Information("Results page shape: {Shape}", shape);
        Log.Information("Paginator: {Paginator}", await PaginatorHtmlAsync(page).ConfigureAwait(false));

        // The page-size box is worth more than the next-page arrow beside it: one
        // request for fifty beats five requests for ten, on a host we have
        // promised to leave alone for five seconds between visits.
        var sizeBox = page.Locator(".paginator-container .page-box").First;

        if (await sizeBox.IsVisibleAsync().ConfigureAwait(false))
        {
            await sizeBox.ClickAsync(new LocatorClickOptions { Timeout = 15000 }).ConfigureAwait(false);
            await page.WaitForTimeoutAsync(1200).ConfigureAwait(false);

            var sizes = await page.EvaluateAsync<string>(
                """
                () => JSON.stringify([...document.querySelectorAll('.dx-list-item, .dx-item-content')]
                  .map(e => (e.innerText || '').trim())
                  .filter(t => /^\d+$/.test(t)))
                """).ConfigureAwait(false);

            Log.Information("Page sizes on offer: {Sizes}", sizes);
        }

        // The shape counts elements; this says what a person would be looking at.
        // When the two disagree - a page reporting no rows while the api answered
        // with ten - the text is what settles it.
        var visible = (await page.InnerTextAsync("body").ConfigureAwait(false))
            .ReplaceLineEndings(" | ")
            .Trim();

        Log.Information("Visible text: {Text}", visible[..Math.Min(visible.Length, 1200)]);

        await Task.CompletedTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return 0;
    }

    /// <summary>
    /// How many the search matched, and the dates of the first few it returned.
    /// </summary>
    /// <remarks>
    /// The dates are the point. Which end of the list EKAP hands over decides
    /// whether a fifteen-minute poll ever sees a notice that closes this week,
    /// and it is not something to take on faith from the look of one screen.
    /// </remarks>
    private static string DescribeSearchBody()
    {
        if (_lastSearchBody is not { Length: > 0 } body)
        {
            return "(no search response was captured)";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var total = root.TryGetProperty("totalCount", out var count) ? count.ToString() : "?";

            if (!root.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return $"total {total}, no list";
            }

            var dates = list.EnumerateArray()
                .Take(4)
                .Select(item => DateField(item) ?? "?")
                .ToArray();

            return $"total {total}, {list.GetArrayLength()} returned, first dates: {string.Join(", ", dates)}";
        }
        catch (JsonException)
        {
            return "(the search response was not json)";
        }
    }

    private static string? DateField(JsonElement item)
    {
        foreach (var name in new[] { "ihaleTarihSaat", "fiyatTeklifiSonTarihSaati", "ilanTarihi" })
        {
            if (item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    /// <summary>The paginator itself, so the next-page control can be identified.</summary>
    private static async Task<string> PaginatorHtmlAsync(IPage page)
        => await page.EvaluateAsync<string>(
            """
            () => {
              const e = document.querySelector('[class*=paginator]');
              if (!e) return '(no paginator)';
              const controls = [...e.querySelectorAll('*')]
                .filter(c => c.tagName === 'BUTTON' || c.tagName === 'A' || c.tagName === 'I'
                          || (c.className || '').toString().includes('page'))
                .slice(0, 12)
                .map(c => ({
                  tag: c.tagName.toLowerCase(),
                  cls: (c.className || '').toString().slice(0, 70),
                  id: c.id || null,
                  text: (c.innerText || '').replace(/\s+/g, ' ').trim().slice(0, 20)
                }));
              return JSON.stringify({ html: e.outerHTML.slice(0, 700), controls });
            }
            """).ConfigureAwait(false);

    /// <summary>What the results header says it is showing.</summary>
    private static async Task<string> CountTextAsync(IPage page)
        => await page.EvaluateAsync<string>(
            """
            () => {
              const e = document.querySelector('[class*=paginator]');
              return e ? (e.innerText || '').replace(/\s+/g, ' ').trim().slice(0, 60) : '(no count)';
            }
            """).ConfigureAwait(false);

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
