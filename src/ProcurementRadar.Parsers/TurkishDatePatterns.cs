using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ProcurementRadar.Parsers;

/// <summary>
/// config/tr-date-patterns.json, compiled. Loading is separate from parsing so a
/// parser never has to touch the disk.
/// </summary>
public sealed class TurkishDatePatterns
{
    private TurkishDatePatterns(
        TimeZoneInfo timeZone,
        IReadOnlyDictionary<string, int> months,
        IReadOnlyList<string> noise,
        IReadOnlyList<DatePattern> patterns)
    {
        TimeZone = timeZone;
        Months = months;
        Noise = noise;
        Patterns = patterns;
    }

    public TimeZoneInfo TimeZone { get; }

    public IReadOnlyDictionary<string, int> Months { get; }

    /// <summary>Weekday names and filler words, longest first so that removing one
    /// cannot leave a fragment of a longer one behind.</summary>
    public IReadOnlyList<string> Noise { get; }

    public IReadOnlyList<DatePattern> Patterns { get; }

    public static TurkishDatePatterns Load(string json)
    {
        var shape = JsonSerializer.Deserialize<Shape>(json)
            ?? throw new InvalidOperationException("tr-date-patterns.json deserialized to null.");

        if (shape.Patterns.Count == 0)
        {
            throw new InvalidOperationException("tr-date-patterns.json lists no patterns.");
        }

        var noise = shape.WeekdaysToStrip
            .Concat(shape.NoiseToStrip)
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .Select(word => word.Trim())
            .Distinct(StringComparer.Ordinal)
            // "tarihine kadar" has to go before "tarihinde" would ever get a look
            // in, or the remainder stops matching anything.
            .OrderByDescending(word => word.Length)
            .ToArray();

        return new TurkishDatePatterns(
            TimeZoneInfo.FindSystemTimeZoneById(shape.TimeZone),
            shape.Months,
            noise,
            shape.Patterns.Select(DatePattern.From).ToArray());
    }

    public sealed class DatePattern
    {
        private DatePattern(string name, Regex regex, IReadOnlyList<string> groups, TimeOnly? defaultTime)
        {
            Name = name;
            Regex = regex;
            Groups = groups;
            DefaultTime = defaultTime;
        }

        public string Name { get; }

        public Regex Regex { get; }

        /// <summary>What capture group 1, 2, 3 … mean. Positions are data, not code.</summary>
        public IReadOnlyList<string> Groups { get; }

        /// <summary>
        /// Used when the pattern carries no time. A guess, and logged as one: a
        /// deadline read as 23:59 when it was really 10:00 looks like fourteen
        /// hours of slack that does not exist.
        /// </summary>
        public TimeOnly? DefaultTime { get; }

        internal static DatePattern From(PatternShape shape)
            => new(
                shape.Name,
                // Case-insensitive: the text is lowercased before matching, so a
                // pattern written with an uppercase separator such as the T in
                // an ISO timestamp would otherwise never fire.
                new Regex(shape.Regex, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
                shape.Groups,
                shape.DefaultTime is { Length: > 0 } time
                    ? TimeOnly.ParseExact(time, "HH:mm", System.Globalization.CultureInfo.InvariantCulture)
                    : null);
    }

    internal sealed record PatternShape
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("regex")]
        public string Regex { get; init; } = string.Empty;

        [JsonPropertyName("groups")]
        public IReadOnlyList<string> Groups { get; init; } = [];

        [JsonPropertyName("default_time")]
        public string? DefaultTime { get; init; }
    }

    private sealed record Shape
    {
        [JsonPropertyName("timezone")]
        public string TimeZone { get; init; } = "Europe/Istanbul";

        [JsonPropertyName("months")]
        public Dictionary<string, int> Months { get; init; } = [];

        [JsonPropertyName("weekdays_to_strip")]
        public IReadOnlyList<string> WeekdaysToStrip { get; init; } = [];

        [JsonPropertyName("noise_to_strip")]
        public IReadOnlyList<string> NoiseToStrip { get; init; } = [];

        [JsonPropertyName("patterns")]
        public IReadOnlyList<PatternShape> Patterns { get; init; } = [];
    }
}
