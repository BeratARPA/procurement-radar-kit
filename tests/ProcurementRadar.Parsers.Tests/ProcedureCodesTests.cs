using ProcurementRadar.Parsers;

namespace ProcurementRadar.Parsers.Tests;

/// <summary>
/// The mapping from what EKAP writes to what the column stores, against the
/// shipped <c>config/tr-procedure-codes.json</c> rather than a stub.
/// </summary>
/// <remarks>
/// A stub here would test the assumption that EKAP writes "22/d*" and
/// "İhale Usulü: Pazarlık (MD 21 E)". Those two strings are the whole risk, and
/// the file the worker loads is where they have to be right.
/// </remarks>
public sealed class ProcedureCodesTests
{
    private static readonly ProcedureCodes Codes = ProcedureCodes.Load(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config", "tr-procedure-codes.json")));

    [Theory]
    // Exactly as the doğrudan temin search writes it, star and parenthetical
    // included. Trimming those off before the lookup is the point.
    [InlineData("22/d* (Parasal Limit Kapsamında)", "art22d")]
    [InlineData("22/c", "art22c")]
    [InlineData("22/D* (Parasal Limit Kapsamında)", "art22d")]
    public void An_article_is_read_off_the_front_of_the_text(string text, string expected)
        => Assert.Equal(expected, Codes.ForArticle(text));

    [Theory]
    [InlineData("İhale Usulü: Açık", "open")]
    [InlineData("İhale Usulü: Belli İstekliler Arasında", "restricted")]
    // The one the fixture actually carries, and the reason the article pattern
    // exists: "negotiated" would have been true and "art21e" is what the notice
    // says.
    [InlineData("İhale Usulü: Pazarlık (MD 21 E)", "art21e")]
    public void A_procedure_sentence_resolves_to_its_code(string text, string expected)
        => Assert.Equal(expected, Codes.ForProcedure(text));

    [Fact]
    public void A_negotiated_tender_with_no_article_still_says_negotiated()
    {
        // The fallback has to survive the article pattern missing, or an EKAP
        // that stops printing the parenthetical would silently empty the column
        // rather than degrade to the coarser answer.
        Assert.Equal("negotiated", Codes.ForProcedure("İhale Usulü: Pazarlık"));
    }

    [Fact]
    public void An_article_nobody_has_listed_comes_back_empty_rather_than_guessed()
    {
        // 22/z does not exist in the law. A fallback to the commonest article
        // would put a notice under a rule it was not bought under, and nothing
        // downstream could tell.
        Assert.Null(Codes.ForArticle("22/z"));
        Assert.Null(Codes.ForProcedure("İhale Usulü: Bilinmeyen"));
        Assert.Null(Codes.ForArticle(null));
        Assert.Null(Codes.ForProcedure("   "));
    }

    [Fact]
    public void Turkish_casing_is_used_to_fold_the_text()
    {
        // The bug this forecloses: under invariant rules "İ" lowercases to two
        // characters, "İHALE USULÜ: AÇIK" never becomes "açık", and every tender
        // shouted in capitals reads as no procedure at all.
        Assert.Equal("open", Codes.ForProcedure("İHALE USULÜ: AÇIK"));
        Assert.Equal("restricted", Codes.ForProcedure("BELLİ İSTEKLİLER ARASINDA"));
    }
}
