using System.Globalization;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Adapters;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Fetching;
using ProcurementRadar.Domain.Settings;
using ProcurementRadar.Parsers;
using Serilog;
using Serilog.Extensions.Logging;

namespace ProcurementRadar.Cli;

/// <summary>
/// Points the generic adapter at a page it has never seen and works out how to
/// read it.
/// </summary>
/// <remarks>
/// Adding a source used to cost an afternoon: find the page, read its html,
/// guess selectors, capture a fixture, watch it for three days. That price is
/// why eighty sources is a target rather than a fact, and why eleven sources sat
/// switched off with notes saying the page had been empty on the day somebody
/// looked.
///
/// This tries every reasonable way to read the page against the real page and
/// scores the results, so what comes out has been measured rather than guessed:
/// how many rows, how many carry a link, how many carry something that parses as
/// a date, how many have a title long enough to be one. It prints the config it
/// would use and how confident it is.
///
/// It fetches through the same fetcher the collector uses, so robots.txt is
/// obeyed and the host is left alone for five seconds afterwards. A tool for
/// adding sources must not be the one thing that gets the project banned.
/// </remarks>
internal static class TrySourceCommand
{
    private static readonly string[] RowSelectors =
    [
        "table tbody tr", "table tr", "ul li", "ol li",
        ".list-group .list-group-item", ".card", "article",
        "div.row", ".news-item", ".duyuru", ".ihale",
        "div.panel", "li.media", ".content ul li", "#content ul li"
    ];

    // Ordered by how specific they are: an anchor inside a heading is a title far
    // more often than the first anchor in the row, which is as likely to be a
    // breadcrumb or a share button.
    private static readonly string[] TitleSelectors =
    [
        "h1 a", "h2 a", "h3 a", "h4 a", "h5 a", ".title a", ".baslik a",
        "a.title", "a", "h2", "h3", "h4", ".title", "td:nth-child(2)", "td"
    ];

    /// <summary>
    /// Where the row's own link is.
    /// </summary>
    /// <remarks>
    /// Three cases, all of them real. Usually an anchor inside the row. Sometimes
    /// the row is the anchor - a whole card wrapped in one link, which :scope
    /// picks up and a descendant search never will; Nallıhan is built that way
    /// and its twelve tenders were invisible because of it. And sometimes there
    /// are no detail pages at all, which is not an error: the notice is recorded
    /// against the listing, where a person would go to read it anyway.
    /// </remarks>
    private static readonly string?[] LinkSelectors = ["a", ":scope", null];

    private static readonly string[] DateSelectors =
    [
        "time", ".date", ".tarih", "span.date", "td:nth-child(1)",
        "td:nth-child(3)", ".meta", "small"
    ];

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var url = ReadOption(args, "--url");

        if (string.IsNullOrWhiteSpace(url))
        {
            Log.Error("Usage: try-source --url <url> [--render] [--charset <name>] [--config-dir <path>]");
            return 1;
        }

        ConfigFiles config;
        try
        {
            config = ConfigFiles.LoadFrom(ReadOption(args, "--config-dir"));
        }
        catch (Exception exception)
            when (exception is IOException or InvalidOperationException or DirectoryNotFoundException)
        {
            Log.Error(exception, "Could not load the config files");
            return 1;
        }

        using var loggerFactory = new SerilogLoggerFactory(Log.Logger);

        var contactEmail = Environment.GetEnvironmentVariable("PR_CONTACT_EMAIL") ?? string.Empty;
        var options = new HttpFetcherOptions { ContactEmail = contactEmail };

        using var httpClient = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        });

        var fetcher = new HttpFetcher(
            httpClient,
            options,
            TimeProvider.System,
            loggerFactory.CreateLogger<HttpFetcher>());

        var probe = new Source
        {
            Id = 0,
            AuthorityName = "try-source",
            AuthorityType = AuthorityType.Municipality,
            Province = "Ankara",
            Url = url,
            AdapterKey = GenericTableAdapter.AdapterKey
        };

        // --render answers a different question: not "can this page be read"
        // but "is there anything on it once the scripts have run". Seven pages
        // read as empty without it and certainly carry tenders.
        var render = args.Contains("--render", StringComparer.Ordinal);

        await using var renderer = new PlaywrightPageRenderer(
            new HostPolitenessGate(TimeProvider.System, options),
            options,
            new EkapBrowserOptions { AnnouncementButtonName = "-" },
            loggerFactory.CreateLogger<PlaywrightPageRenderer>());

        var result = render
            ? await renderer.RenderAsync(url, cancellationToken).ConfigureAwait(false)
            : await fetcher.FetchAsync(new FetchRequest(url), cancellationToken).ConfigureAwait(false);

        if (result.Outcome != FetchOutcome.Fetched || result.Body is null)
        {
            // robots.txt refusing is an answer, not a failure: it means this page
            // is not ours to read and no amount of selector work changes that.
            Log.Warning("Nothing to read at {Url}: {Outcome}", url, result.Outcome);
            return 1;
        }

        var (body, charSet) = BodyDecoder.Decode(result.Body, ReadOption(args, "--charset"));

        Log.Information("{Bytes} bytes as {CharSet}", result.Body.Length, charSet);

        var dates = new TurkishDateParser(
            new Fixed<TurkishDatePatterns>(config.DatePatterns),
            loggerFactory.CreateLogger<TurkishDateParser>());

        var adapter = new GenericTableAdapter(
            new UnusableFetcher(),
            dates,
            loggerFactory.CreateLogger<GenericTableAdapter>());

        var origin = new Uri(url).GetLeftPart(UriPartial.Authority);
        var candidates = new List<Candidate>();

        // The fixed list above covers the shapes that keep recurring; this reads
        // the shapes this particular page actually uses. Guessing found two rows
        // on a page with five on it, which is the whole argument for asking the
        // document instead.
        var rowSelectors = RowSelectors.Concat(Derived(body)).Distinct(StringComparer.Ordinal).ToList();

        var document = new HtmlParser().ParseDocument(body);

        foreach (var rowSelector in rowSelectors)
        {
            foreach (var titleSelector in TitleSelectors)
            {
                foreach (var dateSelector in DateSelectors.Prepend(null))
                foreach (var linkSelector in LinkSelectors)
                {
                    var json = Config(origin, rowSelector, titleSelector, dateSelector, linkSelector);

                    if (!GenericTableAdapterConfig.TryRead(json, out _, out _))
                    {
                        continue;
                    }

                    IReadOnlyList<RawNotice> notices;

                    try
                    {
                        notices = adapter.Parse(body, probe with { AdapterConfig = json });
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        // An unusable selector is one of sixty thousand tried
                        // here; it is not news.
                        continue;
                    }

                    if (notices.Count == 0 || IsFurniture(document, rowSelector))
                    {
                        continue;
                    }

                    candidates.Add(new Candidate(json, rowSelector, titleSelector, dateSelector, Score(notices), notices));
                }
            }
        }

        if (candidates.Count == 0)
        {
            Log.Warning(
                "No arrangement of selectors read anything out of {Url}. Either the list is built by "
                + "javascript, or this is not a listing page.",
                url);

            return 1;
        }

        var best = candidates.OrderByDescending(candidate => candidate.Score).Take(3).ToList();

        foreach (var candidate in best)
        {
            Log.Information(
                "{Score} points | rows={Rows} dated={Dated} | {RowSelector} + {TitleSelector} + {DateSelector}",
                candidate.Score,
                candidate.Notices.Count,
                candidate.Notices.Count(notice => notice.BidDeadlineAt is not null),
                candidate.RowSelector,
                candidate.TitleSelector,
                candidate.DateSelector ?? "(no date)");
        }

        var winner = best[0];

        Log.Information("First three rows it would produce:");

        foreach (var notice in winner.Notices.Take(3))
        {
            Log.Information(
                "  {Title} | {Deadline} | {Url}",
                notice.Title.Length > 70 ? notice.Title[..70] : notice.Title,
                notice.BidDeadlineAt?.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) ?? "-",
                notice.SourceUrl);
        }

        Log.Information("adapter_config: {Config}", winner.Config);

        // Said out loud because the number is the point of the command. A page
        // read by a lucky selector looks exactly like one read by the right
        // selector until the day the page changes.
        Log.Information(
            "Confidence {Score}/100. Below 50, look at the rows above before switching this source on.",
            winner.Score);

        return 0;
    }

    /// <summary>
    /// Selectors read off the page: whatever element shape repeats on it.
    /// </summary>
    /// <remarks>
    /// A listing is a repetition. Whatever the site's designer called their rows,
    /// there are several of them and they share a tag and a class, so counting
    /// signatures finds them without anybody having anticipated the name. The
    /// ones that repeat between three and eighty times and contain a link are
    /// worth trying; the rest are the page's furniture.
    /// </remarks>
    private static IEnumerable<string> Derived(string body)
    {
        var document = new HtmlParser().ParseDocument(body);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var element in document.All)
        {
            // Only names that are a css selector as written. Tailwind sites -
            // which municipal sites are, now - carry classes like
            // group-hover:text-forest and md:flex-1, and a colon in an unescaped
            // selector makes the whole thing invalid. Nallıhan's twelve tenders
            // were invisible for exactly this reason: the selector was built,
            // matched nothing, and said nothing.
            var safe = element.ClassList
                .Where(name => name.Length is > 1 and < 40 && name.All(IsSelectorSafe))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            if (safe.Count == 0)
            {
                continue;
            }

            Count(counts, element.LocalName + "." + string.Join(".", safe));

            // And each class on its own. The combination identifies one component
            // exactly; a single class is what a designer reuses for every row,
            // and on a utility-class site the combination is different on every
            // element while one class is shared.
            foreach (var name in safe)
            {
                Count(counts, element.LocalName + "." + name);
                Count(counts, "." + name);
            }
        }

        return counts
            .Where(pair => pair.Value is >= 3 and <= 80)
            .OrderByDescending(pair => pair.Value)
            .Select(pair => pair.Key)
            .Take(60);
    }

    /// <summary>
    /// Whether this selector is picking up the site's furniture rather than its
    /// content.
    /// </summary>
    /// <remarks>
    /// A navigation menu is a repetition of linked items with short titles, which
    /// is also the description of a listing, and the scoring could not tell them
    /// apart: Bursa read as BELEDİYE and MECLİS, Antalya as Vizyonumuz/Misyonumuz.
    /// The html says which is which, though. Menus live in nav, header, footer
    /// and aside; announcements do not.
    /// </remarks>
    private static bool IsFurniture(IHtmlDocument document, string rowSelector)
    {
        IHtmlCollection<IElement> matches;

        try
        {
            matches = document.QuerySelectorAll(rowSelector);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return true;
        }

        if (matches.Length == 0)
        {
            return true;
        }

        var inChrome = matches.Count(element => element.Ancestors<IElement>().Any(ancestor =>
            ancestor.LocalName is "nav" or "header" or "footer" or "aside"
            || ancestor.ClassList.Any(name =>
                name.Contains("nav", StringComparison.OrdinalIgnoreCase)
                || name.Contains("menu", StringComparison.OrdinalIgnoreCase)
                || name.Contains("footer", StringComparison.OrdinalIgnoreCase)
                || name.Contains("header", StringComparison.OrdinalIgnoreCase))));

        // Half, not all: a listing can sit inside a container that happens to
        // carry a layout class with "header" in its name.
        return inChrome * 2 > matches.Length;
    }

    private static void Count(Dictionary<string, int> counts, string selector)
        => counts[selector] = counts.GetValueOrDefault(selector) + 1;

    private static bool IsSelectorSafe(char character)
        => char.IsAsciiLetterOrDigit(character) || character is '-' or '_';

    /// <summary>
    /// How much this arrangement looks like a list of notices rather than a
    /// navigation menu.
    /// </summary>
    /// <remarks>
    /// Every part of this is a way of being wrong that was seen for real. A menu
    /// parses as thirty rows of four-word titles. A footer parses as five rows
    /// that all point at the same page. A breadcrumb parses as one row. Dates are
    /// the strongest single signal that a list is a list of announcements.
    /// </remarks>
    private static int Score(IReadOnlyList<RawNotice> notices)
    {
        var score = 0;

        // Somewhere between a handful and a page of them. One row is a heading;
        // two hundred is the whole site's navigation.
        score += notices.Count switch
        {
            >= 3 and <= 60 => 25,
            1 or 2 => 5,
            _ => 0
        };

        var titles = notices.Select(notice => notice.Title.Trim()).ToList();

        // Announcement titles are sentences. Menu items are one or two words.
        var wordy = titles.Count(title => title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 4);
        score += (int)(25.0 * wordy / titles.Count);

        // Distinct links. A footer repeats one target; a list does not.
        var links = notices.Select(notice => notice.SourceUrl).Distinct(StringComparer.Ordinal).Count();
        score += (int)(20.0 * links / notices.Count);

        // A date on most rows is what tells a listing from a menu.
        var dated = notices.Count(notice => notice.BidDeadlineAt is not null);
        score += (int)(30.0 * dated / notices.Count);

        return Math.Min(score, 100);
    }

    private static string Config(
        string baseUrl,
        string rowSelector,
        string titleSelector,
        string? dateSelector,
        string? linkSelector)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["base_url"] = baseUrl,
            ["row_selector"] = rowSelector,
            ["title_selector"] = titleSelector,
            ["link_selector"] = linkSelector,
            ["date_selector"] = dateSelector,
            ["time_selector"] = null,
            ["amount_selector"] = null,
            ["document_selector"] = null
        });

    private sealed record Candidate(
        string Config,
        string RowSelector,
        string TitleSelector,
        string? DateSelector,
        int Score,
        IReadOnlyList<RawNotice> Notices);

    /// <summary>Parse is a pure function; this makes a stray request impossible.</summary>
    private sealed class UnusableFetcher : IHttpFetcher
    {
        public Task<FetchResult> FetchAsync(FetchRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("try-source fetches once, up front; Parse does not.");
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
