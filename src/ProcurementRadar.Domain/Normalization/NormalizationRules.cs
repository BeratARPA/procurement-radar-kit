using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProcurementRadar.Domain.Normalization;

/// <summary>
/// config/tr-normalization.json, loaded. Loading is separate from normalising so
/// nothing on the parse path has to touch the disk.
/// </summary>
public sealed class NormalizationRules
{
    private NormalizationRules(
        IReadOnlyDictionary<char, string> asciiFolding,
        IReadOnlyList<string> suffixes)
    {
        AsciiFolding = asciiFolding;
        Suffixes = suffixes;
    }

    public IReadOnlyDictionary<char, string> AsciiFolding { get; }

    /// <summary>Longest first, so "hizmet alımı" is tried before "alımı".</summary>
    public IReadOnlyList<string> Suffixes { get; }

    public static NormalizationRules Load(string json)
    {
        var shape = JsonSerializer.Deserialize<Shape>(json)
            ?? throw new InvalidOperationException("tr-normalization.json deserialized to null.");

        var folding = shape.AsciiFolding
            .Where(entry => entry.Key.Length == 1)
            .ToDictionary(entry => entry.Key[0], entry => entry.Value);

        var suffixes = shape.SuffixesToStrip
            .Where(suffix => !string.IsNullOrWhiteSpace(suffix))
            .Select(suffix => suffix.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(suffix => suffix.Length)
            .ToArray();

        return new NormalizationRules(folding, suffixes);
    }

    private sealed record Shape
    {
        [JsonPropertyName("ascii_folding")]
        public Dictionary<string, string> AsciiFolding { get; init; } = [];

        [JsonPropertyName("suffixes_to_strip")]
        public IReadOnlyList<string> SuffixesToStrip { get; init; } = [];
    }
}
