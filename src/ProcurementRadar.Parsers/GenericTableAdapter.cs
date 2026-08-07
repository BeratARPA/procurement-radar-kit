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

    public GenericTableAdapter(
        IHttpFetcher fetcher,
        ITurkishDateParser dateParser,
        ILogger<GenericTableAdapter> logger)
    {
        _fetcher = fetcher;
        _dateParser = dateParser;
        _logger = logger;
    }

    public string Key => AdapterKey;

    public Task<FetchResult> FetchAsync(
        Source source,
        ConditionalRequest conditional,
        CancellationToken cancellationToken)
        => _fetcher.FetchAsync(
            new FetchRequest(source.Url, conditional.ETag, conditional.LastModified),
            cancellationToken);

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
            _logger.LogWarning(
                "Source {SourceId}: {Skipped} row(s) skipped, {Kept} kept",
                source.Id,
                skipped,
                notices.Count);
        }

        document.Dispose();

        return notices;
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
