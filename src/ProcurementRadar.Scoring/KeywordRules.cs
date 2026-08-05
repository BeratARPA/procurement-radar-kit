using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProcurementRadar.Scoring;

public sealed record WeightedTerm(string Term, int Weight);

public sealed record OkasRules(
    IReadOnlyList<string> IncludePrefixes,
    IReadOnlyList<string> ExcludePrefixes,
    int IncludeScore)
{
    /// <summary>
    /// True once somebody has filled the lists in. While they are empty an
    /// notice's OKAS codes are ignored and scoring falls back to the keywords,
    /// which is the behaviour that was there before.
    /// </summary>
    public bool IsConfigured => IncludePrefixes.Count > 0 || ExcludePrefixes.Count > 0;
}

/// <summary>config/keywords.json, loaded.</summary>
public sealed class KeywordRules
{
    private KeywordRules(
        IReadOnlyList<WeightedTerm> include,
        IReadOnlyList<string> exclude,
        OkasRules okas,
        int ruleScoreThreshold,
        int notifyThreshold)
    {
        Include = include;
        Exclude = exclude;
        Okas = okas;
        RuleScoreThreshold = ruleScoreThreshold;
        NotifyThreshold = notifyThreshold;
    }

    public IReadOnlyList<WeightedTerm> Include { get; }

    public IReadOnlyList<string> Exclude { get; }

    public OkasRules Okas { get; }

    /// <summary>Below this a notice is not worth a person's attention.</summary>
    public int RuleScoreThreshold { get; }

    /// <summary>Below this no Telegram message is sent, though the notice is still stored.</summary>
    public int NotifyThreshold { get; }

    public static KeywordRules Load(string json)
    {
        var shape = JsonSerializer.Deserialize<Shape>(json)
            ?? throw new InvalidOperationException("keywords.json deserialized to null.");

        if (shape.Include.Count == 0)
        {
            throw new InvalidOperationException("keywords.json lists no include terms; nothing would ever score.");
        }

        return new KeywordRules(
            shape.Include
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Term))
                .Select(entry => new WeightedTerm(entry.Term!.Trim(), entry.Weight))
                // Longest first so the most specific match is reported first in
                // the rationale a person actually reads.
                .OrderByDescending(entry => entry.Term.Length)
                .ToArray(),
            shape.Exclude
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Select(term => term.Trim())
                .ToArray(),
            new OkasRules(
                shape.Okas.IncludePrefixes.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray(),
                shape.Okas.ExcludePrefixes.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray(),
                shape.Okas.IncludeScore),
            shape.RuleScoreThreshold,
            shape.NotifyThreshold);
    }

    private sealed record TermShape
    {
        [JsonPropertyName("term")]
        public string? Term { get; init; }

        [JsonPropertyName("weight")]
        public int Weight { get; init; }
    }

    private sealed record OkasShape
    {
        [JsonPropertyName("include_prefixes")]
        public IReadOnlyList<string> IncludePrefixes { get; init; } = [];

        [JsonPropertyName("exclude_prefixes")]
        public IReadOnlyList<string> ExcludePrefixes { get; init; } = [];

        [JsonPropertyName("include_score")]
        public int IncludeScore { get; init; } = 90;
    }

    private sealed record Shape
    {
        [JsonPropertyName("include")]
        public IReadOnlyList<TermShape> Include { get; init; } = [];

        [JsonPropertyName("exclude")]
        public IReadOnlyList<string> Exclude { get; init; } = [];

        [JsonPropertyName("okas")]
        public OkasShape Okas { get; init; } = new();

        [JsonPropertyName("rule_score_threshold")]
        public int RuleScoreThreshold { get; init; } = 30;

        [JsonPropertyName("notify_threshold")]
        public int NotifyThreshold { get; init; } = 60;
    }
}
