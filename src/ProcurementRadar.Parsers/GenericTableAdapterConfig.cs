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

    /// <summary>The link to the detail page inside a row.</summary>
    public required string LinkSelector { get; init; }

    /// <summary>Where relative links are resolved against.</summary>
    public required string BaseUrl { get; init; }

    /// <summary>Optional: many listings carry no usable date.</summary>
    public string? DateSelector { get; init; }

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
                     ("link_selector", shape.LinkSelector),
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
            LinkSelector = shape.LinkSelector!.Trim(),
            BaseUrl = shape.BaseUrl.Trim(),
            DateSelector = Optional(shape.DateSelector),
            AmountSelector = Optional(shape.AmountSelector)
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

        [JsonPropertyName("amount_selector")]
        public string? AmountSelector { get; init; }

        [JsonPropertyName("base_url")]
        public string? BaseUrl { get; init; }
    }
}
