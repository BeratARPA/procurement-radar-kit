using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ProcurementRadar.Domain.Adapters;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Fetching;
using ProcurementRadar.Domain.Parsing;

namespace ProcurementRadar.Parsers;

/// <summary>
/// Reads EKAP, the state's own platform and the only source that covers every
/// authority in the country at once.
/// </summary>
/// <remarks>
/// The archive here holds json rather than html, because what EKAP's own client
/// receives is json - the page a person looks at is assembled from it in the
/// browser. That is still the raw body as the server sent it, so D-11 holds:
/// what is stored is what arrived, and reprocessing it later is possible.
///
/// Fetching needs a real browser and the reasons are in D-12. Everything below
/// <see cref="Parse"/> is a pure function over the saved response, which is why
/// this file has no browser in it and its tests need none either. spec.md
/// Bölüm 13 calls this the most fragile component and says to keep the tests
/// tightest here; splitting it this way is what makes that possible at all.
/// </remarks>
public sealed class EkapAdapter : ISourceAdapter
{
    public const string AdapterKey = "ekap_tender";

    /// <summary>
    /// The app's own request that carries the results. Matched as a fragment
    /// because the rest of the url is theirs to change.
    /// </summary>
    public const string ApiUrlFragment = "/b_ihalearama/api/Ihale/GetListByParameters";

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private readonly IEkapSearchClient _search;
    private readonly ITurkishDateParser _dateParser;
    private readonly ILogger<EkapAdapter> _logger;

    public EkapAdapter(
        IEkapSearchClient search,
        ITurkishDateParser dateParser,
        ILogger<EkapAdapter> logger)
    {
        _search = search;
        _dateParser = dateParser;
        _logger = logger;
    }

    public string Key => AdapterKey;

    public Task<FetchResult> FetchAsync(
        Source source,
        ConditionalRequest conditional,
        CancellationToken cancellationToken)
    {
        // No conditional request: there is no url to send If-None-Match for. The
        // browser drives a form and reads what comes back, so change detection
        // rests entirely on content_hash - which is what T-06 does anyway.
        if (!EkapAdapterConfig.TryRead(source.AdapterConfig, out var config, out var problem))
        {
            _logger.LogError(
                "Source {SourceId} has no usable adapter_config: {Problem}",
                source.Id,
                problem);

            return Task.FromResult(FetchResult.Failed(0));
        }

        return _search.SearchAsync(
            new EkapSearchRequest(source.Url, config.SearchText, ApiUrlFragment, config.PrepareClickSelector),
            cancellationToken);
    }

    public IReadOnlyList<RawNotice> Parse(string body, Source source)
    {
        if (!EkapAdapterConfig.TryRead(source.AdapterConfig, out var config, out var problem))
        {
            _logger.LogError(
                "Source {SourceId} has no usable adapter_config: {Problem}",
                source.Id,
                problem);

            return [];
        }

        SearchResponse? response;

        try
        {
            response = JsonSerializer.Deserialize<SearchResponse>(body);
        }
        catch (JsonException exception)
        {
            // The shape changed, or a maintenance page came back where json was
            // expected. Never throw: the pipeline has to finish every other source.
            _logger.LogError(
                exception,
                "Source {SourceId} returned something that is not the EKAP search response",
                source.Id);

            return [];
        }

        if (response?.List is null)
        {
            _logger.LogError(
                "Source {SourceId} returned a body with no result list. EKAP's response shape "
                + "has probably changed; this is the alarm spec.md section 13 asks for.",
                source.Id);

            return [];
        }

        var notices = new List<RawNotice>();
        var filteredOut = 0;

        foreach (var item in response.List)
        {
            if (string.IsNullOrWhiteSpace(item.Title))
            {
                continue;
            }

            if (!Keep(item, config))
            {
                filteredOut++;
                continue;
            }

            notices.Add(new RawNotice(
                Title: item.Title.Trim(),
                // EKAP's response carries no per-notice address - only an opaque
                // id whose route is not published anywhere we could check. A url
                // guessed from it would look right and rot silently, so the
                // notice is recorded against the search page, and the ikn below
                // is what actually identifies it.
                SourceUrl: source.Url,
                AuthorityName: string.IsNullOrWhiteSpace(item.AuthorityName)
                    ? source.AuthorityName
                    : item.AuthorityName.Trim(),
                Description: Describe(item),
                BidDeadlineAt: _dateParser.TryParse(item.TenderDateTime),
                EstimatedCost: null,
                DocumentUrls: [],
                Ikn: string.IsNullOrWhiteSpace(item.Ikn) ? null : item.Ikn.Trim()));
        }

        _logger.LogInformation(
            "EKAP search for source {SourceId}: {Total} match(es) reported, {Returned} on this page, "
            + "{Kept} kept, {Filtered} filtered out by province or status",
            source.Id,
            response.TotalCount,
            response.List.Count,
            notices.Count,
            filteredOut);

        return notices;
    }

    private static bool Keep(SearchItem item, EkapAdapterConfig config)
    {
        if (config.Province is { } province
            && !string.Equals(
                (item.Province ?? string.Empty).Trim().ToUpper(Turkish),
                province.ToUpper(Turkish),
                StringComparison.Ordinal))
        {
            return false;
        }

        if (config.Statuses.Count == 0)
        {
            return true;
        }

        var status = (item.Status ?? string.Empty).Trim();

        // Matched as a prefix: EKAP writes the state and then a comma and a
        // qualifier, and the qualifier is the part that changes.
        return config.Statuses.Any(wanted =>
            status.StartsWith(wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// What kind of tender, run how, and where it currently stands - the three
    /// things a person asks before reading further. The rule scorer reads this
    /// alongside the title.
    /// </summary>
    private static string? Describe(SearchItem item)
    {
        var parts = new[] { item.TenderType, item.Procedure, item.Status, item.Province }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim());

        var text = string.Join(" | ", parts);

        return text.Length == 0 ? null : text;
    }

    private sealed record SearchResponse
    {
        [JsonPropertyName("list")]
        public IReadOnlyList<SearchItem>? List { get; init; }

        [JsonPropertyName("totalCount")]
        public long TotalCount { get; init; }
    }

    /// <summary>
    /// One row of EKAP's search response. The property names are theirs, not
    /// ours, which is why they are the only Turkish words in this file.
    /// </summary>
    private sealed record SearchItem
    {
        [JsonPropertyName("ihaleAdi")]
        public string? Title { get; init; }

        [JsonPropertyName("ikn")]
        public string? Ikn { get; init; }

        [JsonPropertyName("idareAdi")]
        public string? AuthorityName { get; init; }

        [JsonPropertyName("ihaleIlAdi")]
        public string? Province { get; init; }

        [JsonPropertyName("ihaleTarihSaat")]
        public string? TenderDateTime { get; init; }

        [JsonPropertyName("ihaleTipAciklama")]
        public string? TenderType { get; init; }

        [JsonPropertyName("ihaleUsulAciklama")]
        public string? Procedure { get; init; }

        [JsonPropertyName("ihaleDurumAciklama")]
        public string? Status { get; init; }
    }
}
