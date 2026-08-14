using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ProcurementRadar.Parsers;

/// <summary>
/// Turns the Turkish procedure names EKAP publishes into the codes
/// <c>procurement_notice.procedure_code</c> stores, from
/// <c>config/tr-procedure-codes.json</c>.
/// </summary>
/// <remarks>
/// Same reason as the keyword lists, the date patterns and the award labels: the
/// source text is Turkish, so the words are data and the machinery is English.
///
/// Both search responses have carried this all along and nothing stored it. Of
/// 437 archived EKAP items, 422 name their procedure and 15 name the article
/// they were bought under - and the article is the distinction spec.md Bölüm 0
/// says the whole project exists for. An open tender and a 22/d purchase are not
/// the same job for a three-person team working evenings, and until now no
/// screen could tell them apart.
/// </remarks>
public sealed record ProcedureCodes
{
    // Turkish casing, not invariant: "AÇIK" folds to "açık" here and "İhale"
    // to "ihale". Under the invariant rules İ lowercases to two characters and
    // matches nothing in the config.
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Article prefixes as EKAP writes them, e.g. <c>22/d</c>.</summary>
    [JsonPropertyName("articles")]
    public required IReadOnlyDictionary<string, string> Articles { get; init; }

    /// <summary>Fragments that can appear in a procedure sentence.</summary>
    [JsonPropertyName("procedures")]
    public required IReadOnlyDictionary<string, string> Procedures { get; init; }

    /// <summary>
    /// Reads the article out of a procedure sentence that states one in
    /// parentheses, e.g. <c>Pazarlık (MD 21 E)</c>.
    /// </summary>
    [JsonPropertyName("article_in_procedure")]
    public required string ArticleInProcedure { get; init; }

    private Regex? _articleInProcedure;

    private Regex ArticlePattern => _articleInProcedure ??= new Regex(
        ArticleInProcedure,
        RegexOptions.CultureInvariant,
        // A pattern that comes from a settable document is a pattern somebody
        // can make pathological without meaning to. Everything else here is
        // ordinal string work, so a hang would be this line or nothing.
        TimeSpan.FromMilliseconds(250));

    public static ProcedureCodes Load(string json)
    {
        var codes = JsonSerializer.Deserialize<ProcedureCodes>(json)
            ?? throw new InvalidOperationException("tr-procedure-codes.json is empty.");

        if (codes.Articles.Count == 0 || codes.Procedures.Count == 0)
        {
            throw new InvalidOperationException(
                "tr-procedure-codes.json must list both articles and procedures.");
        }

        // Compiled here rather than on first use, so a broken pattern is refused
        // when the setting is saved (D-16) instead of throwing inside Parse.
        _ = codes.ArticlePattern;

        return codes;
    }

    /// <summary>
    /// The code for a direct-procurement article, or null when the text names
    /// none we recognise.
    /// </summary>
    /// <remarks>
    /// EKAP writes "22/d* (Parasal Limit Kapsamında)", so this matches on the
    /// front of the string rather than the whole of it. Null rather than a guess:
    /// an article we have never seen belongs in the config, not in a fallback
    /// that quietly files it under the commonest one.
    /// </remarks>
    public string? ForArticle(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var folded = text.Trim().ToLower(Turkish);

        return Articles
            .Where(entry => folded.StartsWith(entry.Key, StringComparison.Ordinal))
            // Longest key first, so a future "22/dd" could never be shadowed by
            // "22/d". Dictionary order is not something to rely on for this.
            .OrderByDescending(entry => entry.Key.Length)
            .Select(entry => entry.Value)
            .FirstOrDefault();
    }

    /// <summary>
    /// The code for a tender procedure sentence, or null when nothing matches.
    /// </summary>
    public string? ForProcedure(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var folded = text.ToLower(Turkish);

        // The article, when the sentence names one. "Pazarlık (MD 21 E)" is
        // article 21/e, which is a strictly better answer than "negotiated" -
        // spec.md Bölüm 4 gives art21f as one of its own examples. Tried first
        // because the fallback below would happily answer the vaguer thing.
        var article = ArticlePattern.Match(folded);

        if (article.Success
            && Articles.TryGetValue(
                $"{article.Groups["article"].Value}/{article.Groups["letter"].Value}",
                out var byArticle))
        {
            return byArticle;
        }

        // Longest fragment wins, which is the specific one: a sentence naming
        // "belli istekliler arasında" would also contain a shorter fragment one
        // day, and whichever came first in the file would decide. Json object
        // order is preserved in practice and guaranteed nowhere, so the rule
        // that resolves an overlap has to be in the code rather than in the
        // order somebody happened to type the keys.
        return Procedures
            .Where(entry => folded.Contains(entry.Key, StringComparison.Ordinal))
            .OrderByDescending(entry => entry.Key.Length)
            .Select(entry => entry.Value)
            .FirstOrDefault();
    }
}
