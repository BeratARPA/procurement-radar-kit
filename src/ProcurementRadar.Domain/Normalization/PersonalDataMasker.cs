using System.Text.RegularExpressions;
using ProcurementRadar.Domain.Adapters;

namespace ProcurementRadar.Domain.Normalization;

/// <summary>
/// Removes the officials' names, phone numbers and addresses that notice pages
/// print, before anything reaches the database (spec Bölüm 12).
/// </summary>
/// <remarks>
/// Applied centrally, on the way out of the pipeline, rather than inside each
/// adapter: sixteen adapters means sixteen chances to forget, and there is no
/// undoing a personal detail that has been stored. Over-masking is the safe
/// direction - a lost sentence is lost information, a stored phone number is a
/// data protection problem.
/// </remarks>
public sealed class PersonalDataMasker
{
    // Turkish numbers as pages print them: 0312 123 45 67, (0312) 1234567,
    // +90 532 123 45 67. The leading word boundary matters: without it the
    // pattern can start mid-number and swallow part of an IKN like 2026/123456.
    private static readonly Regex PhonePattern = new(
        @"\b(?:\+90|0090|0)?\s*\(?\d{3}\)?[\s./-]*\d{3}[\s./-]*\d{2}[\s./-]*\d{2}\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex EmailPattern = new(
        @"\b[\w.+-]+@[\w-]+\.[\w.-]+\b",
        RegexOptions.CultureInvariant);

    // Named by code point rather than written out. These are a casing rule, not
    // vocabulary, so they belong in code - but U+0130 and U+0131 are almost
    // impossible to tell from I and i in a monospaced font, and the whole
    // codebase otherwise keeps Turkish text in config/.
    private const char DotlessSmallI = (char)0x0131;
    private const char DottedCapitalI = (char)0x0130;

    private static readonly string AnyIShapedLetter =
        string.Concat("[iI", DotlessSmallI, DottedCapitalI, "]");

    private readonly PersonalDataRules _rules;
    private readonly Regex _labelPattern;

    public PersonalDataMasker(PersonalDataRules rules)
    {
        _rules = rules;

        // Everything from a contact label up to the end of the sentence or the
        // line. Bounded so a label near the top of a page cannot blank the rest
        // of the description.
        var labels = string.Join('|', rules.ContactLabels.Select(AnyTurkishCasing));

        // The value stops one character short of a following outline number, and
        // that guard is why this reads the way it does.
        //
        // A tender announcement is a numbered list flattened into one line:
        //
        //     1.3. Telefon: 0312 ... 1.4. İhale dokümanının ...
        //
        // "up to the end of the sentence" ended at the first full stop, which is
        // the one *inside* 1.4 - so the value swallowed that leading 1 and the
        // stored text read "Telefon: ***.4. İhale dokümanının". Item 1.4 lost
        // the number it is found by. Twice per announcement, in 236 of the 270
        // documents held.
        //
        // The guard is deliberately narrow: it needs a trailing dot as well, so
        // it blocks on " 1.4." and " 4.1.2.1." and not on a plain decimal like
        // " 2.5 milyon", which would otherwise cut the mask short. Cutting the
        // mask short is the direction that risks leaving a name behind, so it
        // gets the narrow rule.
        _labelPattern = new Regex(
            $@"\b(?<label>{labels})\b\s*[:\-]?\s*(?<value>(?:(?!\s\d{{1,2}}(?:\.\d{{1,2}})+\.)[^.;\r\n]){{0,80}})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public RawNotice Mask(RawNotice notice) => notice with
    {
        Title = MaskText(notice.Title) ?? notice.Title,
        Description = MaskText(notice.Description)
    };

    /// <summary>
    /// Escapes a label and widens every i-shaped letter to cover all four.
    /// </summary>
    /// <remarks>
    /// Case-insensitive matching with the invariant rules folds Ş to ş and Ü to
    /// ü correctly, but not İ to i: the lowercase of İ is two characters, so a
    /// character-wise fold leaves it alone and "İrtibat" never matches
    /// "irtibat". Turning to Turkish casing instead would make the behaviour
    /// depend on the machine's current culture, which is worse - a name masked
    /// on the developer's laptop and stored on an English-locale server. The
    /// class is explicit for that reason.
    /// </remarks>
    private static string AnyTurkishCasing(string label)
    {
        var escaped = Regex.Escape(label);

        return string.Concat(escaped.Select(character =>
            character is 'i' or 'I' or DotlessSmallI or DottedCapitalI
                ? AnyIShapedLetter
                : character.ToString()));
    }

    public string? MaskText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var masked = EmailPattern.Replace(text, _rules.Mask);
        masked = PhonePattern.Replace(masked, _rules.Mask);

        return _labelPattern.Replace(
            masked,
            match => match.Groups["value"].Value.Trim().Length == 0
                ? match.Value
                : $"{match.Groups["label"].Value}: {_rules.Mask}");
    }
}
