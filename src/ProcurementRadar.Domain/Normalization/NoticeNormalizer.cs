using System.Globalization;
using System.Text;

namespace ProcurementRadar.Domain.Normalization;

/// <summary>
/// Reduces a title or an authority name to the form two sources have to agree on
/// before the same notice can be recognised twice.
/// </summary>
public sealed class NoticeNormalizer
{
    // Turkish casing, not invariant: "İHALE" lowercases to "ihale" here and to
    // "i̇hale" under the invariant rules, which folds to something else entirely.
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private readonly NormalizationRules _rules;

    public NoticeNormalizer(NormalizationRules rules)
    {
        _rules = rules;
    }

    /// <summary>
    /// Lowercases, folds Turkish letters to ASCII, drops punctuation, collapses
    /// runs of whitespace, and removes the trailing words that say nothing about
    /// which notice this is.
    /// </summary>
    public string Normalize(string? text)
    {
        var folded = Fold(text);

        return folded.Length == 0 ? folded : StripSuffixes(folded);
    }

    /// <summary>Everything except the suffix removal. Useful where the trailing
    /// words carry meaning, such as an authority name.</summary>
    public string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lowered = text.ToLower(Turkish);
        var builder = new StringBuilder(lowered.Length);
        var lastWasSpace = true;

        foreach (var character in lowered)
        {
            if (_rules.AsciiFolding.TryGetValue(character, out var replacement))
            {
                builder.Append(replacement);
                lastWasSpace = false;
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasSpace = false;
                continue;
            }

            // Punctuation and whitespace alike become a single separator, so
            // "Yazılım/Donanım" and "Yazılım - Donanım" end up the same.
            if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private string StripSuffixes(string folded)
    {
        var current = folded;

        // Repeated on purpose: "yazilim bakim ve destek hizmeti alimi" has to lose
        // "alimi" and then "hizmeti".
        for (var pass = 0; pass < _rules.Suffixes.Count; pass++)
        {
            var stripped = false;

            foreach (var suffix in _rules.Suffixes)
            {
                var foldedSuffix = Fold(suffix);
                if (foldedSuffix.Length == 0
                    || !current.EndsWith(" " + foldedSuffix, StringComparison.Ordinal))
                {
                    continue;
                }

                var candidate = current[..^(foldedSuffix.Length + 1)].TrimEnd();

                // A title that is nothing but suffix words keeps them. Emptying it
                // would make every such notice from one authority collide on the
                // same day.
                if (candidate.Length == 0)
                {
                    return current;
                }

                current = candidate;
                stripped = true;
                break;
            }

            if (!stripped)
            {
                break;
            }
        }

        return current;
    }
}
