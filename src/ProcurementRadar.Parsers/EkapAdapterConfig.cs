using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProcurementRadar.Parsers;

/// <summary>
/// What one EKAP source row searches for. Held in <c>source.adapter_config</c>
/// like every other adapter's settings.
/// </summary>
/// <remarks>
/// One search term per source row, deliberately. EKAP's box searches the notice
/// title and the teklif cetveli together, so "yazılım" already finds work whose
/// title says nothing useful - and a second term is a second row in the registry
/// rather than a change to any code, with its own poll interval and its own
/// health record.
/// </remarks>
public sealed record EkapAdapterConfig
{
    private const string Placeholder = "TODO";

    /// <summary>What EKAP's page-size box actually contains. Measured, not assumed.</summary>
    private static readonly int[] OfferedPageSizes = [10, 20, 50];

    /// <summary>What to type into EKAP's search box.</summary>
    public required string SearchText { get; init; }

    /// <summary>
    /// Keep only notices from this province, compared case-insensitively in
    /// Turkish. Null keeps everything.
    /// </summary>
    /// <remarks>
    /// Filtering here rather than in the search form is a deliberate trade. The
    /// province control is a DevExtreme dropdown whose markup changes with the
    /// component library; the search box and its button are a text input and a
    /// button. Fewer moving parts in the half that cannot be unit tested, and the
    /// province arrives in the response anyway.
    /// </remarks>
    public string? Province { get; init; }

    /// <summary>
    /// Statuses worth recording, matched against <c>ihaleDurumAciklama</c> as a
    /// prefix. Empty keeps everything.
    /// </summary>
    /// <remarks>
    /// EKAP returns cancelled and concluded tenders alongside open ones, and a
    /// cancelled tender is not work - it is noise with a deadline attached.
    /// </remarks>
    public IReadOnlyList<string> Statuses { get; init; } = [];

    /// <summary>
    /// Something to click before the search box exists, or null when the page
    /// arrives ready to search.
    /// </summary>
    /// <remarks>
    /// The doğrudan temin page needs it. Opening it directly shows only the
    /// notice that the new application went into pilot on 17.07.2026, with a
    /// button to reach the listing, and no search box at all until that button is
    /// pressed. It lives here rather than in the browser client because it is a
    /// fact about one page on one site, and those are data in this project - the
    /// same reason the CSS selectors of every other source are in this column.
    /// </remarks>
    public string? PrepareClickSelector { get; init; }

    /// <summary>
    /// A filter on EKAP's own panel to switch on before searching, by its visible
    /// label. Null searches everything, as before.
    /// </summary>
    /// <remarks>
    /// This is the difference between a radar and a museum. Measured on
    /// 08.08.2026: "otomasyon" matches 9425 tenders, EKAP returns ten of them,
    /// and it returns them sorted by tender date with the furthest future first -
    /// so the ten we saw were all dated 02.09 to 07.09, a month out. A tender
    /// closing next Tuesday was not missing from the page; it was on page four
    /// hundred of nine hundred and forty-three.
    ///
    /// Switching on the tender-date filter takes the same search from 9425 to 29
    /// and from 943 pages to 3, because everything already closed drops out. That
    /// is the set this project was built to watch.
    ///
    /// The label is Turkish because it is the site's own text, which makes it
    /// data (CLAUDE.md rule 5). It will change when they redesign the panel, and
    /// changing it must not mean a release.
    /// </remarks>
    public string? FilterLabel { get; init; }

    /// <summary>
    /// How many results per page to ask EKAP for. Null leaves their default of
    /// ten.
    /// </summary>
    /// <remarks>
    /// Their paginator offers 10, 20 and 50. Fifty costs one extra interaction
    /// and saves four round trips against a host we have promised to visit no
    /// more than once every five seconds - the arithmetic is not close.
    /// </remarks>
    public int? PageSize { get; init; }

    public static bool TryRead(string? json, out EkapAdapterConfig config, out string problem)
    {
        config = null!;

        if (string.IsNullOrWhiteSpace(json))
        {
            problem = "adapter_config is empty";
            return false;
        }

        Shape? shape;

        try
        {
            shape = JsonSerializer.Deserialize<Shape>(json);
        }
        catch (JsonException exception)
        {
            problem = $"adapter_config is not valid JSON: {exception.Message}";
            return false;
        }

        if (shape is null)
        {
            problem = "adapter_config is not valid JSON";
            return false;
        }

        if (string.IsNullOrWhiteSpace(shape.SearchText))
        {
            problem = "search_text is missing";
            return false;
        }

        if (string.Equals(shape.SearchText.Trim(), Placeholder, StringComparison.OrdinalIgnoreCase))
        {
            problem = "search_text is still TODO";
            return false;
        }

        config = new EkapAdapterConfig
        {
            SearchText = shape.SearchText.Trim(),
            Province = string.IsNullOrWhiteSpace(shape.Province) ? null : shape.Province.Trim(),
            Statuses = shape.Statuses?
                .Where(status => !string.IsNullOrWhiteSpace(status))
                .Select(status => status.Trim())
                .ToArray() ?? [],
            PrepareClickSelector = string.IsNullOrWhiteSpace(shape.PrepareClickSelector)
                ? null
                : shape.PrepareClickSelector.Trim(),
            FilterLabel = string.IsNullOrWhiteSpace(shape.FilterLabel) ? null : shape.FilterLabel.Trim(),
            // A page size EKAP does not offer would be typed into their box and
            // silently ignored, leaving us to believe we asked for fifty.
            PageSize = shape.PageSize is { } size && OfferedPageSizes.Contains(size) ? size : null
        };

        problem = string.Empty;
        return true;
    }

    private sealed record Shape
    {
        [JsonPropertyName("search_text")]
        public string? SearchText { get; init; }

        [JsonPropertyName("province")]
        public string? Province { get; init; }

        [JsonPropertyName("statuses")]
        public string[]? Statuses { get; init; }

        [JsonPropertyName("prepare_click_selector")]
        public string? PrepareClickSelector { get; init; }

        [JsonPropertyName("filter_label")]
        public string? FilterLabel { get; init; }

        [JsonPropertyName("page_size")]
        public int? PageSize { get; init; }
    }
}
