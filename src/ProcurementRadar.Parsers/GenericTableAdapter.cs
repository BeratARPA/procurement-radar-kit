using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using ProcurementRadar.Domain.Adapters;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Fetching;
using ProcurementRadar.Domain.Parsing;

namespace ProcurementRadar.Parsers;

/// <summary>
/// Handles the roughly seventy per cent of sources that are a plain list of
/// date, title and link. The selectors come from <c>source.adapter_config</c>,
/// so a new source of this shape needs a row in the database rather than a
/// class.
/// </summary>
public sealed class GenericTableAdapter : ISourceAdapter
{
    public const string AdapterKey = "generic_table";

    private static readonly Regex CollapseWhitespace = new(@"\s+", RegexOptions.CultureInvariant);

    private readonly IHttpFetcher _fetcher;
    private readonly ITurkishDateParser _dateParser;
    private readonly ILogger<GenericTableAdapter> _logger;

    private readonly IPageRenderer? _renderer;

    /// <param name="renderer">
    /// Optional. Without one, a source asking to be rendered is fetched plainly
    /// and will find nothing - which is visible rather than silent, because the
    /// never-produced alarm covers a source that reads as empty for a week.
    /// </param>
    public GenericTableAdapter(
        IHttpFetcher fetcher,
        ITurkishDateParser dateParser,
        ILogger<GenericTableAdapter> logger,
        IPageRenderer? renderer = null)
    {
        _fetcher = fetcher;
        _dateParser = dateParser;
        _logger = logger;
        _renderer = renderer;
    }

    public string Key => AdapterKey;

    public Task<FetchResult> FetchAsync(
        Source source,
        ConditionalRequest conditional,
        CancellationToken cancellationToken)
    {
        // A conditional request is skipped when rendering: there is no ETag on
        // something a browser assembled, and the whole point is to run the
        // scripts. Change detection still works, over the rendered bytes.
        if (_renderer is not null
            && GenericTableAdapterConfig.TryRead(source.AdapterConfig, out var config, out _)
            && config!.Render)
        {
            return _renderer.RenderAsync(source.Url, cancellationToken);
        }

        return _fetcher.FetchAsync(
            new FetchRequest(source.Url, conditional.ETag, conditional.LastModified),
            cancellationToken);
    }

    public IReadOnlyList<RawNotice> Parse(string body, Source source)
    {
        if (!GenericTableAdapterConfig.TryRead(source.AdapterConfig, out var config, out var problem))
        {
            _logger.LogError(
                "Source {SourceId} has no usable adapter_config: {Problem}",
                source.Id,
                problem);

            return [];
        }

        IDocument document;
        IEnumerable<IElement> rows;

        try
        {
            document = new HtmlParser().ParseDocument(body);
            rows = document.QuerySelectorAll(config.RowSelector);
        }
        catch (Exception exception)
        {
            // Almost always an invalid selector. Parse never throws: the pipeline
            // has to keep going for every other source.
            _logger.LogError(
                exception,
                "Source {SourceId} could not be read with row selector '{RowSelector}'",
                source.Id,
                config.RowSelector);

            return [];
        }

        var notices = new List<RawNotice>();
        var skipped = 0;

        foreach (var row in rows)
        {
            try
            {
                if (TryReadRow(row, config, source, out var notice))
                {
                    notices.Add(notice);
                }
                else
                {
                    skipped++;
                }
            }
            catch (Exception exception)
            {
                // One bad row must never discard the rest of the page.
                skipped++;
                _logger.LogWarning(
                    exception,
                    "Skipped an unreadable row on source {SourceId}",
                    source.Id);
            }
        }

        if (skipped > 0)
        {
            // A warning only when *everything* was skipped.
            //
            // Several row selectors are broader than the rows they are after -
            // Nallıhan matches a Tailwind `.transition-all`, Kocaeli a
            // `.col-md-4` grid column - so each fetch also matches the header,
            // the sidebar and the footer, and drops them for having no title.
            // That is imprecise and it is not a fault: the notices are all
            // kept, and the skipped elements were never notices. It was 464
            // skipped against 377 kept on one source in three days, every line
            // of it at warning level, counted in the weekly report (D-07).
            //
            // Nothing kept is different. Then the selector matched things and
            // none of them was readable, which is the shape of a page that has
            // been redesigned underneath us.
            _logger.Log(
                notices.Count == 0 ? LogLevel.Warning : LogLevel.Debug,
                "Source {SourceId}: {Skipped} row(s) skipped, {Kept} kept",
                source.Id,
                skipped,
                notices.Count);
        }

        document.Dispose();

        return notices;
    }

    /// <summary>
    /// Whether the listing container is on the page with no rows inside it.
    /// </summary>
    /// <remarks>
    /// The container is the row selector with its last step removed:
    /// <c>div.common-list ul li</c> describes rows, and <c>div.common-list ul</c>
    /// describes the list they sit in. If the list is there and the rows are
    /// not, the page is intact and empty; if the list is gone too, the page has
    /// changed underneath us and that is what the alarm is for.
    ///
    /// A single-step selector has no container to fall back to, so it answers
    /// false - unknown, keep alarming. Same for a selector whose container part
    /// will not compile. The safe direction here is the noisy one: a missed
    /// alarm is a source that quietly stops collecting, and one of those cost
    /// three months in the story spec.md Bölüm 11 opens with.
    /// </remarks>
    public bool ListWasEmpty(string body, Source source)
    {
        if (!GenericTableAdapterConfig.TryRead(source.AdapterConfig, out var config, out _))
        {
            return false;
        }

        var lastStep = config.RowSelector.LastIndexOf(' ');

        if (lastStep <= 0)
        {
            return false;
        }

        var container = config.RowSelector[..lastStep].Trim();

        try
        {
            using var document = new HtmlParser().ParseDocument(body);

            return document.QuerySelectorAll(container).Length > 0
                && document.QuerySelectorAll(config.RowSelector).Length == 0;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Source {SourceId}: could not test container selector '{Container}'",
                source.Id,
                container);

            return false;
        }
    }

    private bool TryReadRow(
        IElement row,
        GenericTableAdapterConfig config,
        Source source,
        out RawNotice notice)
    {
        notice = null!;

        var title = Text(row, config.TitleSelector);
        if (string.IsNullOrWhiteSpace(title))
        {
            // Header rows and spacer rows land here. Not worth a warning.
            return false;
        }

        // A listing without detail pages is common enough to be normal, not an
        // error. The notice is then recorded against the listing itself, which
        // is where a person would go to read it anyway.
        var sourceUrl = source.Url;

        if (config.LinkSelector is { } linkSelector)
        {
            var href = row.QuerySelector(linkSelector)?.GetAttribute("href");

            if (!TryResolve(config.BaseUrl, href, out sourceUrl))
            {
                return false;
            }
        }

        // Never DateTime.Parse: the formats are data (config/tr-date-patterns.json)
        // and the conversion out of Europe/Istanbul has to happen in one place.
        var deadline = config.DateSelector is null
            ? TurkishDateParseResult.Unreadable
            : _dateParser.Read(DateText(row, config));

        var amount = config.AmountSelector is null
            ? null
            : TurkishMoneyParser.TryParse(Text(row, config.AmountSelector));

        notice = new RawNotice(
            Title: title,
            SourceUrl: sourceUrl,
            AuthorityName: source.AuthorityName,
            BidDeadlineAt: deadline.Value,
            BidDeadlineTimeAssumed: deadline.TimeWasAssumed,
            EstimatedCost: amount,
            DocumentUrls: DocumentUrls(row, config));

        return true;
    }

    /// <summary>
    /// The text the date parser is given, which is the date cell plus the time
    /// cell when the listing keeps them apart.
    /// </summary>
    /// <remarks>
    /// Splitting them across two columns is common in these tables - Ankara
    /// Bilkent Şehir Hastanesi heads them "SON TEKLİF VERME TARİHİ" and "SON
    /// TEKLİF VERME SAATİ" - and reading only the first is not a small loss. A
    /// date with no time falls back to 23:59, so a notice that actually closes
    /// at 12:00 is recorded as closing eleven hours later, and the notifier
    /// reports time remaining on something already shut. That is the dangerous
    /// direction of the two: it does not make you hurry, it makes you relax.
    /// </remarks>
    private static string? DateText(IElement row, GenericTableAdapterConfig config)
    {
        var date = Text(row, config.DateSelector!);

        if (config.TimeSelector is not { } timeSelector || string.IsNullOrWhiteSpace(date))
        {
            return date;
        }

        var time = Text(row, timeSelector);

        return string.IsNullOrWhiteSpace(time) ? date : $"{date} {time}";
    }

    private static IReadOnlyList<string> DocumentUrls(IElement row, GenericTableAdapterConfig config)
    {
        if (config.DocumentSelector is not { } selector)
        {
            return [];
        }

        var urls = new List<string>();

        foreach (var element in row.QuerySelectorAll(selector))
        {
            if (TryResolve(config.BaseUrl, element.GetAttribute("href"), out var url))
            {
                urls.Add(url);
            }
        }

        return urls;
    }

    private static string? Text(IElement row, string selector)
    {
        var element = row.QuerySelector(selector);
        if (element is null)
        {
            return null;
        }

        var text = CollapseWhitespace.Replace(element.TextContent ?? string.Empty, " ").Trim();

        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// A stored relative link is useless the moment the listing page changes, so
    /// everything is resolved against base_url on the way out.
    /// </summary>
    private static bool TryResolve(string baseUrl, string? href, out string absolute)
    {
        absolute = string.Empty;

        if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#'))
        {
            return false;
        }

        if (!Uri.TryCreate(new Uri(baseUrl, UriKind.Absolute), href.Trim(), out var resolved))
        {
            return false;
        }

        if (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        absolute = resolved.ToString();
        return true;
    }
}
