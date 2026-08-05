using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProcurementRadar.Domain.Normalization;

/// <summary>config/tr-personal-data.json, loaded.</summary>
public sealed class PersonalDataRules
{
    private PersonalDataRules(string mask, IReadOnlyList<string> contactLabels)
    {
        Mask = mask;
        ContactLabels = contactLabels;
    }

    public string Mask { get; }

    /// <summary>Longest first, so "irtibat kişisi" is matched before "irtibat".</summary>
    public IReadOnlyList<string> ContactLabels { get; }

    public static PersonalDataRules Load(string json)
    {
        var shape = JsonSerializer.Deserialize<Shape>(json)
            ?? throw new InvalidOperationException("tr-personal-data.json deserialized to null.");

        var labels = shape.ContactLabels
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(label => label.Length)
            .ToArray();

        return new PersonalDataRules(
            string.IsNullOrEmpty(shape.Mask) ? "***" : shape.Mask,
            labels);
    }

    private sealed record Shape
    {
        [JsonPropertyName("mask")]
        public string Mask { get; init; } = "***";

        [JsonPropertyName("contact_labels")]
        public IReadOnlyList<string> ContactLabels { get; init; } = [];
    }
}
