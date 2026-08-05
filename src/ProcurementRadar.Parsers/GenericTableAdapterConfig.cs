using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProcurementRadar.Parsers;

/// <summary>
/// The CSS selectors for one listing page, read from <c>source.adapter_config</c>.
/// The field names match docs/source-collection.md, which is what the team fills
/// in by hand.
/// </summary>
public sealed record GenericTableAdapterConfig
{
    /// <summary>The placeholder the team leaves in place until a page is inspected.</summary>
    public const string Placeholder = "TODO";

    /// <summary>The element wrapping one notice, for example <c>table tbody tr</c>.</summary>
    public required string RowSelector { get; init; }

    /// <summary>The title inside a row.</summary>
    public required string TitleSelector { get; init; }

    /// <summary>
    /// The link to the detail page inside a row, where there is one.
    /// </summary>
    /// <remarks>
    /// Optional, because plenty of listings have no detail page at all: the
    /// Doğrudan Temin table at Ankara İl Sağlık Müdürlüğü, for one, links
    /// straight to the specification files. When it is absent the notice is
    /// recorded against the listing page itself, which is still where a person
    /// would go to read it.
    /// </remarks>
    public string? LinkSelector { get; init; }

    /// <summary>
    /// Links in a row that point at specification files rather than at a page.
    /// </summary>
    /// <remarks>
    /// Where a listing publishes the documents directly, this is what fills
    /// notice_document and gives the downloader in T-12 something to work with.
    /// </remarks>
    public string? DocumentSelector { get; init; }

    /// <summary>Where relative links are resolved against.</summary>
    public required string BaseUrl { get; init; }

    /// <summary>Optional: many listings carry no usable date.</summary>
    public string? DateSelector { get; init; }

    /// <summary>
    /// Optional: where a listing puts the time of day in its own column, apart
    /// from the date. Its text is appended to the date before parsing.
    /// </summary>
    /// <remarks>
    /// Ignored unless <see cref="DateSelector"/> is set. Leaving it out where
    /// the site does split them is not a harmless simplification: the date alone
    /// falls back to 23:59, so a deadline of 12:00 is stored as most of a day
    /// later and the notifier counts down to an hour that has already passed.
    /// </remarks>
    public string? TimeSelector { get; init; }

    /// <summary>Optional: most listings carry no amount at all.</summary>
    public string? AmountSelector { get; init; }

    public static bool TryRead(
        string? json,
        out GenericTableAdapterConfig config,
        out string problem)
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
            problem = "adapter_config is null";
            return false;
        }

        foreach (var (name, value) in new[]
                 {
                     ("row_selector", shape.RowSelector),
                     ("title_selector", shape.TitleSelector),
                     ("base_url", shape.BaseUrl)
                 })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                problem = $"{name} is missing";
                return false;
            }

            // A source still carrying placeholders would parse to zero notices,
            // which is indistinguishable from a broken parser. Say so instead.
            if (string.Equals(value.Trim(), Placeholder, StringComparison.OrdinalIgnoreCase))
            {
                problem = $"{name} is still {Placeholder}; the page has not been inspected yet";
                return false;
            }
        }

        if (!Uri.TryCreate(shape.BaseUrl!.Trim(), UriKind.Absolute, out _))
        {
            problem = $"base_url '{shape.BaseUrl}' is not an absolute url";
            return false;
        }

        config = new GenericTableAdapterConfig
        {
            RowSelector = shape.RowSelector!.Trim(),
            TitleSelector = shape.TitleSelector!.Trim(),
            BaseUrl = shape.BaseUrl.Trim(),
            LinkSelector = Optional(shape.LinkSelector),
            DateSelector = Optional(shape.DateSelector),
            TimeSelector = Optional(shape.TimeSelector),
            AmountSelector = Optional(shape.AmountSelector),
            DocumentSelector = Optional(shape.DocumentSelector)
        };

        problem = string.Empty;
        return true;
    }

    private static string? Optional(string? value)
        => string.IsNullOrWhiteSpace(value)
            || string.Equals(value.Trim(), Placeholder, StringComparison.OrdinalIgnoreCase)
                ? null
                : value.Trim();

    private sealed record Shape
    {
        [JsonPropertyName("row_selector")]
        public string? RowSelector { get; init; }

        [JsonPropertyName("title_selector")]
        public string? TitleSelector { get; init; }

        [JsonPropertyName("link_selector")]
        public string? LinkSelector { get; init; }

        [JsonPropertyName("date_selector")]
        public string? DateSelector { get; init; }

        [JsonPropertyName("time_selector")]
        public string? TimeSelector { get; init; }

        [JsonPropertyName("amount_selector")]
        public string? AmountSelector { get; init; }

        [JsonPropertyName("document_selector")]
        public string? DocumentSelector { get; init; }

        [JsonPropertyName("base_url")]
        public string? BaseUrl { get; init; }
    }
}
