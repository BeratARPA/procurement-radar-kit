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

    private readonly Domain.Settings.ICurrent<KeywordRules> _current;
    private readonly NoticeNormalizer _normalizer;
    private CompiledRules _compiled;

    public RuleScorer(Domain.Settings.ICurrent<KeywordRules> rules, NoticeNormalizer normalizer)
    {
        _current = rules;
        _normalizer = normalizer;
        _compiled = Compile(rules.Value, normalizer);
    }

    public KeywordRules Rules => Current().Rules;

    private IReadOnlyList<(WeightedTerm Term, Regex Pattern)> Include => Current().Include;

    private IReadOnlyList<(string Term, Regex Pattern)> Exclude => Current().Exclude;

    /// <summary>
    /// The compiled form of whatever the keyword list is right now.
    /// </summary>
    /// <remarks>
    /// Turning thirty terms into thirty regexes is not free, and it does not
    /// belong on the path of every notice. Compiled once per version of the
    /// list and kept until the list is replaced - which, once the settings
    /// screen exists, is a handful of times a week rather than never.
    ///
    /// The check is reference equality against the object the setting handed
    /// out, so a save that produces an identical list still recompiles, and no
    /// change never does. Two threads arriving together may both compile; they
    /// produce the same thing and the second simply wins.
    /// </remarks>
    private CompiledRules Current()
    {
        var rules = _current.Value;
        var compiled = _compiled;

        if (ReferenceEquals(compiled.Rules, rules))
        {
            return compiled;
        }

        compiled = Compile(rules, _normalizer);
        _compiled = compiled;

        return compiled;
    }

    private static CompiledRules Compile(KeywordRules rules, NoticeNormalizer normalizer) => new(
        rules,
        rules.Include.Select(term => (term, Matcher(normalizer, term.Term))).ToArray(),
        rules.Exclude.Select(term => (term, Matcher(normalizer, term))).ToArray());

    private sealed record CompiledRules(
        KeywordRules Rules,
        IReadOnlyList<(WeightedTerm Term, Regex Pattern)> Include,
        IReadOnlyList<(string Term, Regex Pattern)> Exclude);

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
        if (okasCodes is { Count: > 0 } codes && Rules.Okas.IsConfigured)
        {
            if (FirstPrefixMatch(codes, Rules.Okas.ExcludePrefixes) is { } excludedCode)
            {
                return new RuleScore(0, ScoreBasis.Excluded, [], excludedCode);
            }

            if (FirstPrefixMatch(codes, Rules.Okas.IncludePrefixes) is { } includedCode)
            {
                return new RuleScore(
                    Math.Clamp(Rules.Okas.IncludeScore, 0, MaximumScore),
                    ScoreBasis.Okas,
                    [includedCode]);
            }
        }

        var text = _normalizer.Fold($"{title} {description}");

        // Exclude wins outright. "Bilgisayar yazılım ve donanım alımı" matches
        // both lists and is hardware nine times out of ten.
        foreach (var (term, pattern) in Exclude)
        {
            if (pattern.IsMatch(text))
            {
                return new RuleScore(0, ScoreBasis.Excluded, [], term);
            }
        }

        var matched = new List<string>();
        var total = 0;

        foreach (var (term, pattern) in Include)
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
