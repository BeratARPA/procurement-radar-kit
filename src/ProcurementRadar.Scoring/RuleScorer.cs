using System.Text.RegularExpressions;
using ProcurementRadar.Domain.Normalization;

namespace ProcurementRadar.Scoring;

public enum ScoreBasis
{
    /// <summary>Scored from the OKAS codes, which do not depend on wording.</summary>
    Okas,

    /// <summary>Scored from words in the title and description.</summary>
    Keywords,

    /// <summary>Ruled out. The score is zero whatever else matched.</summary>
    Excluded
}

public sealed record RuleScore(
    int Value,
    ScoreBasis Basis,
    IReadOnlyList<string> MatchedTerms,
    string? ExcludedBy = null)
{
    /// <summary>What goes in <c>notice_score.rationale</c>: the terms that
    /// produced the number, in the language the notice was written in.</summary>
    public string Rationale => ExcludedBy is not null
        ? ExcludedBy
        : string.Join(", ", MatchedTerms);
}

/// <summary>
/// The cheap filter, and the only one in v1 (D-03). Everything it knows lives in
/// config/keywords.json.
/// </summary>
/// <remarks>
/// Filter quality matters more than coverage. Without the exclude list four
/// notifications in five are hardware purchases, and a fortnight later nobody
/// reads them.
/// </remarks>
public sealed class RuleScorer : Domain.Scoring.INoticeScorer
{
    /// <summary>
    /// Written to <c>notice_score.model</c>. Bump it when the scoring changes
    /// enough that older numbers should not be compared with newer ones.
    /// </summary>
    public const string ModelName = "rules@1";

    private const int MaximumScore = 100;

    private readonly KeywordRules _rules;
    private readonly NoticeNormalizer _normalizer;
    private readonly IReadOnlyList<(WeightedTerm Term, Regex Pattern)> _include;
    private readonly IReadOnlyList<(string Term, Regex Pattern)> _exclude;

    public RuleScorer(KeywordRules rules, NoticeNormalizer normalizer)
    {
        _rules = rules;
        _normalizer = normalizer;

        _include = rules.Include.Select(term => (term, Matcher(normalizer, term.Term))).ToArray();
        _exclude = rules.Exclude.Select(term => (term, Matcher(normalizer, term))).ToArray();
    }

    public KeywordRules Rules => _rules;

    public Domain.Scoring.NoticeScoreResult Score(Domain.Entities.ProcurementNotice notice)
    {
        var score = Score(notice.Title, notice.Description, notice.OkasCodes);

        return new Domain.Scoring.NoticeScoreResult(score.Value, score.Rationale, ModelName);
    }

    public RuleScore Score(
        string title,
        string? description = null,
        IReadOnlyList<string>? okasCodes = null)
    {
        // Spec Bölüm 9: a code beats a word. It says the same thing however the
        // authority chose to phrase the title.
        if (okasCodes is { Count: > 0 } codes && _rules.Okas.IsConfigured)
        {
            if (FirstPrefixMatch(codes, _rules.Okas.ExcludePrefixes) is { } excludedCode)
            {
                return new RuleScore(0, ScoreBasis.Excluded, [], excludedCode);
            }

            if (FirstPrefixMatch(codes, _rules.Okas.IncludePrefixes) is { } includedCode)
            {
                return new RuleScore(
                    Math.Clamp(_rules.Okas.IncludeScore, 0, MaximumScore),
                    ScoreBasis.Okas,
                    [includedCode]);
            }
        }

        var text = _normalizer.Fold($"{title} {description}");

        // Exclude wins outright. "Bilgisayar yazılım ve donanım alımı" matches
        // both lists and is hardware nine times out of ten.
        foreach (var (term, pattern) in _exclude)
        {
            if (pattern.IsMatch(text))
            {
                return new RuleScore(0, ScoreBasis.Excluded, [], term);
            }
        }

        var matched = new List<string>();
        var total = 0;

        foreach (var (term, pattern) in _include)
        {
            if (!pattern.IsMatch(text))
            {
                continue;
            }

            matched.Add(term.Term);
            total += term.Weight;
        }

        return new RuleScore(Math.Clamp(total, 0, MaximumScore), ScoreBasis.Keywords, matched);
    }

    private static string? FirstPrefixMatch(IReadOnlyList<string> codes, IReadOnlyList<string> prefixes)
        => codes.FirstOrDefault(code => prefixes.Any(prefix =>
            code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Matches a term at the start of a word, letting the rest of the word run
    /// on.
    /// </summary>
    /// <remarks>
    /// Turkish glues its endings on, so "yazılım" has to find "yazılımı" and
    /// "yazılımın". A plain substring search would do that, but it would also
    /// find "api" inside "yapım" and score a building contract as software. The
    /// word boundary at the front is what separates the two.
    /// </remarks>
    private static Regex Matcher(NoticeNormalizer normalizer, string term)
        => new(
            $@"\b{Regex.Escape(normalizer.Fold(term))}",
            RegexOptions.CultureInvariant);
}
