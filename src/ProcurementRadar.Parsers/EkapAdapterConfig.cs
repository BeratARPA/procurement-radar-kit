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
                : shape.PrepareClickSelector.Trim()
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
    }
}
