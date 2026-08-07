using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ProcurementRadar.Domain.Adapters;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Fetching;
using ProcurementRadar.Domain.Parsing;

namespace ProcurementRadar.Parsers;

/// <summary>
/// Where a listing arrives as json rather than as a page.
/// </summary>
/// <remarks>
/// Municipal sites are being rebuilt as single-page applications, and the shape
/// is always the same: the server sends a shell of one or two kilobytes and the
/// application asks a second address for the data. Six such pages turned up in a
/// day, and two of them gave up their address to watch-requests -
/// yenimahalle-web-api.alsft.com/api/Portal/GetActiveTenders returning
/// content.tenderListe, and bskapiv1.baskentdogalgaz.com.tr/api/menus/127/sections
/// returning sections.
///
/// Reading the json directly is better than rendering the page in every way that
/// matters: no browser, no wait for scripts, no guessing at selectors, and the
/// fields are named by whoever built it rather than inferred from markup. The
/// endpoint is also a plain http request, so the ordinary fetcher and its
/// politeness rules apply unchanged.
///
/// The path and the field names are configuration, like every selector in this
/// project, because they belong to somebody else's application and will change
/// without warning.
/// </remarks>
public sealed class JsonListAdapter : ISourceAdapter
{
    public const string AdapterKey = "json_list";

    private readonly IHttpFetcher _fetcher;
    private readonly ITurkishDateParser _dateParser;
    private readonly ILogger<JsonListAdapter> _logger;

    public JsonListAdapter(
        IHttpFetcher fetcher,
        ITurkishDateParser dateParser,
        ILogger<JsonListAdapter> logger)
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
        if (!JsonListAdapterConfig.TryRead(source.AdapterConfig, out var config, out var problem))
        {
            _logger.LogError("Source {SourceId} has no usable adapter_config: {Problem}", source.Id, problem);

            return [];
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "Source {SourceId} did not return json", source.Id);

            return [];
        }

        using (document)
        {
            if (!TryWalk(document.RootElement, config!.ListPath, out var list)
                || list.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning(
                    "Source {SourceId}: nothing at '{Path}'. The application's shape has probably changed.",
                    source.Id,
                    config.ListPath);

                return [];
            }

            var notices = new List<RawNotice>();

            foreach (var item in list.EnumerateArray())
            {
                if (TryRead(item, config, source, out var notice))
                {
                    notices.Add(notice!);
                }
            }

            return notices;
        }
    }

    /// <summary>Follows a dotted path like <c>content.tenderListe</c>.</summary>
    private static bool TryWalk(JsonElement element, string path, out JsonElement found)
    {
        found = element;

        foreach (var step in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (found.ValueKind != JsonValueKind.Object || !found.TryGetProperty(step, out found))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryRead(JsonElement item, JsonListAdapterConfig config, Source source, out RawNotice? notice)
    {
        notice = null;

        if (Text(item, config.TitleField) is not { Length: > 0 } title)
        {
            // A row with no title is not a notice. Architecture rule: skip it,
            // do not discard the rest of the list for it.
            return false;
        }

        // Where the endpoint gives an id and the site builds a url from it. The
        // alternative - recording every notice against the endpoint - would give
        // a person a page of json to read.
        var url = config.LinkTemplate is { Length: > 0 } template && Text(item, config.IdField) is { Length: > 0 } id
            ? template.Replace("{id}", id, StringComparison.Ordinal)
            : source.Url;

        var deadline = config.DateField is { Length: > 0 } dateField
            ? _dateParser.Read(Text(item, dateField))
            : TurkishDateParseResult.Unreadable;

        notice = new RawNotice(
            Title: title.Trim(),
            SourceUrl: url,
            AuthorityName: source.AuthorityName,
            Description: config.DescriptionField is { Length: > 0 } field ? Text(item, field) : null,
            BidDeadlineAt: deadline.Value,
            BidDeadlineTimeAssumed: deadline.TimeWasAssumed);

        return true;
    }

    /// <summary>
    /// A field as text, whatever the json says it is.
    /// </summary>
    /// <remarks>
    /// An id is a number in one application and a string in the next, and a date
    /// arrives as both. GetString() throws on the wrong kind, which would lose
    /// the whole list over one field.
    /// </remarks>
    private static string? Text(JsonElement item, string? field)
    {
        if (field is not { Length: > 0 } || !item.TryGetProperty(field, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.GetRawText()
        };
    }
}

/// <summary>Which part of somebody else's json holds the notices.</summary>
public sealed record JsonListAdapterConfig
{
    /// <summary>Dotted path to the array, for example <c>content.tenderListe</c>.</summary>
    [JsonPropertyName("list_path")]
    public string ListPath { get; init; } = string.Empty;

    [JsonPropertyName("title_field")]
    public string TitleField { get; init; } = string.Empty;

    [JsonPropertyName("description_field")]
    public string? DescriptionField { get; init; }

    [JsonPropertyName("date_field")]
    public string? DateField { get; init; }

    /// <summary>Where the id goes, for example <c>https://site/IhaleDetay/{id}</c>.</summary>
    [JsonPropertyName("link_template")]
    public string? LinkTemplate { get; init; }

    [JsonPropertyName("id_field")]
    public string? IdField { get; init; }

    public static bool TryRead(string? json, out JsonListAdapterConfig? config, out string problem)
    {
        config = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            problem = "adapter_config is empty";
            return false;
        }

        try
        {
            config = JsonSerializer.Deserialize<JsonListAdapterConfig>(json);
        }
        catch (JsonException exception)
        {
            problem = exception.Message;
            return false;
        }

        if (config is null || string.IsNullOrWhiteSpace(config.ListPath) || string.IsNullOrWhiteSpace(config.TitleField))
        {
            problem = "list_path and title_field are both required";
            return false;
        }

        problem = string.Empty;
        return true;
    }
}
