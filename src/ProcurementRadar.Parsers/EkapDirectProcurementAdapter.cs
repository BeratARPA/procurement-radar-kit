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
/// Reads EKAP's doğrudan temin search - the one listing that is exactly what this
/// project was built for.
/// </summary>
/// <remarks>
/// Separate from <see cref="EkapAdapter"/> because it is a different service with
/// a different response, not a variation on one. It is also the better of the two
/// to read: the deadline arrives as ISO 8601 with an offset rather than as
/// Turkish free text, and every row carries the article it was let under
/// (22/d and so on).
///
/// Its coverage is the catch. EKAP opened this application as a pilot on
/// 17.07.2026 and only the authorities in that pilot appear, so today it sees a
/// fraction of what the authorities' own pages do. That is why both stay in the
/// registry: where they overlap, the doğrudan temin number deduplicates them.
/// </remarks>
public sealed class EkapDirectProcurementAdapter : ISourceAdapter
{
    public const string AdapterKey = "ekap_direct_procurement";

    public const string ApiUrlFragment = "/b_dogrudantemin/api/DogrudanTemin/GetListByParametersElastic";

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private readonly IEkapSearchClient _search;
    private readonly ITurkishDateParser _dateParser;
    private readonly ILogger<EkapDirectProcurementAdapter> _logger;

    public EkapDirectProcurementAdapter(
        IEkapSearchClient search,
        ITurkishDateParser dateParser,
        ILogger<EkapDirectProcurementAdapter> logger)
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
        if (!EkapAdapterConfig.TryRead(source.AdapterConfig, out var config, out var problem))
        {
            _logger.LogError(
                "Source {SourceId} has no usable adapter_config: {Problem}",
                source.Id,
                problem);

            return Task.FromResult(FetchResult.Failed(0));
        }

        return _search.SearchAsync(
            new EkapSearchRequest(
                source.Url,
                config.SearchText,
                ApiUrlFragment,
                config.PrepareClickSelector,
                config.FilterLabel,
                config.PageSize),
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
            _logger.LogError(
                exception,
                "Source {SourceId} returned something that is not the EKAP direct-procurement response",
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

            if (config.Province is { } province
                && !string.Equals(
                    (item.Province?.Name ?? string.Empty).Trim().ToUpper(Turkish),
                    province.ToUpper(Turkish),
                    StringComparison.Ordinal))
            {
                filteredOut++;
                continue;
            }

            if (config.Statuses.Count > 0
                && !config.Statuses.Any(wanted =>
                    (item.Status ?? string.Empty).Trim().StartsWith(wanted, StringComparison.OrdinalIgnoreCase)))
            {
                filteredOut++;
                continue;
            }

            notices.Add(new RawNotice(
                Title: item.Title.Trim(),
                SourceUrl: source.Url,
                AuthorityName: string.IsNullOrWhiteSpace(item.AuthorityName)
                    ? source.AuthorityName
                    : item.AuthorityName.Trim(),
                Description: Describe(item),
                BidDeadlineAt: Deadline(item),
                EstimatedCost: null,
                DocumentUrls: [],
                // The state's own number for this purchase. It plays the part ikn
                // plays for a tender: where it exists it is the identity, and the
                // same purchase seen on the authority's own page collapses onto
                // it (spec Bölüm 8).
                Ikn: string.IsNullOrWhiteSpace(item.Number) ? null : item.Number.Trim()));
        }

        _logger.LogInformation(
            "EKAP direct-procurement search for source {SourceId}: {Total} match(es) reported, "
            + "{Returned} on this page, {Kept} kept, {Filtered} filtered out",
            source.Id,
            response.TotalCount,
            response.List.Count,
            notices.Count,
            filteredOut);

        return notices;
    }

    /// <summary>
    /// The moment bidding closes. EKAP states it here as ISO 8601 with an offset,
    /// which is the only source in this project that does.
    /// </summary>
    /// <remarks>
    /// Parsed with <see cref="DateTimeStyles.RoundtripKind"/> so the offset in the
    /// text decides, never the machine's time zone (D-08). The Turkish parser is
    /// kept as a fallback rather than as the main path: if EKAP ever writes this
    /// field the way it writes kayitTarihi, the deadline should degrade to a
    /// readable date instead of disappearing.
    /// </remarks>
    private DateTimeOffset? Deadline(SearchItem item)
    {
        if (string.IsNullOrWhiteSpace(item.DeadlineAt))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
                item.DeadlineAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        _logger.LogWarning(
            "EKAP gave a deadline that is not ISO 8601: {Value}. Falling back to the Turkish parser.",
            item.DeadlineAt);

        return _dateParser.TryParse(item.DeadlineAt);
    }

    private static string? Describe(SearchItem item)
    {
        var parts = new[] { item.Article, item.Status, item.Province?.Name }
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
    /// One row of EKAP's doğrudan temin response. The property names are theirs.
    /// </summary>
    private sealed record SearchItem
    {
        [JsonPropertyName("alimAdi")]
        public string? Title { get; init; }

        [JsonPropertyName("dogrudanTeminNumarasi")]
        public string? Number { get; init; }

        [JsonPropertyName("idareAdi")]
        public string? AuthorityName { get; init; }

        [JsonPropertyName("fiyatTeklifiSonTarihSaati")]
        public string? DeadlineAt { get; init; }

        [JsonPropertyName("maddeDayanagiAdi")]
        public string? Article { get; init; }

        [JsonPropertyName("surecDurumuAciklama")]
        public string? Status { get; init; }

        [JsonPropertyName("il")]
        public ProvinceRef? Province { get; init; }
    }

    private sealed record ProvinceRef
    {
        [JsonPropertyName("adi")]
        public string? Name { get; init; }
    }
}
