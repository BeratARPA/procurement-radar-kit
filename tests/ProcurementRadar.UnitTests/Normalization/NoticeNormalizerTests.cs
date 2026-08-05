using ProcurementRadar.Domain.Normalization;

namespace ProcurementRadar.UnitTests.Normalization;

public sealed class NoticeNormalizerTests
{
    internal static readonly NormalizationRules Rules = NormalizationRules.Load(
        File.ReadAllText(Path.Combine(
            RepositoryRoot.Find().FullName, "config", "tr-normalization.json")));

    private static readonly NoticeNormalizer Normalizer = new(Rules);

    [Theory]
    [InlineData("Yazılım", "yazilim")]
    [InlineData("İhale Şartnamesi", "ihale sartnamesi")]
    [InlineData("ÇEVRE VE ŞEHİRCİLİK", "cevre ve sehircilik")]
    [InlineData("Ağustos", "agustos")]
    [InlineData("Gümüşhane", "gumushane")]
    public void Turkish_letters_fold_to_ascii(string input, string expected)
    {
        Assert.Equal(expected, Normalizer.Fold(input));
    }

    [Fact]
    public void The_dotted_and_dotless_i_both_end_up_as_i()
    {
        // The classic Turkish casing trap. "IŞIK" lowercases to "ışık" under
        // Turkish rules; if the folding table missed ı, the two spellings of the
        // same word would produce different keys.
        Assert.Equal("isik", Normalizer.Fold("IŞIK"));
        Assert.Equal("isik", Normalizer.Fold("ışık"));
        Assert.Equal("istanbul", Normalizer.Fold("İstanbul"));
    }

    [Theory]
    [InlineData("Yazılım / Donanım", "yazilim donanim")]
    [InlineData("Yazılım - Donanım", "yazilim donanim")]
    [InlineData("  Yazılım   Bakım  ", "yazilim bakim")]
    [InlineData("Yazılım, Bakım; Destek.", "yazilim bakim destek")]
    public void Punctuation_and_spacing_stop_mattering(string input, string expected)
    {
        Assert.Equal(expected, Normalizer.Fold(input));
    }

    [Theory]
    [InlineData("Yazılım Bakım ve Destek Hizmeti Alımı", "yazilim bakim ve destek")]
    [InlineData("Personel Takip Sistemi Yazılım Alımı İşi", "personel takip sistemi yazilim")]
    [InlineData("Web Sitesi Yenileme Hizmet Alımı", "web sitesi yenileme")]
    [InlineData("Mobil Uygulama Geliştirme İşi", "mobil uygulama gelistirme")]
    public void Trailing_words_that_say_nothing_are_dropped(string input, string expected)
    {
        Assert.Equal(expected, Normalizer.Normalize(input));
    }

    [Fact]
    public void A_title_that_is_nothing_but_trailing_words_keeps_them()
    {
        // Emptying it would make every such notice from one authority collide on
        // the same day.
        Assert.NotEqual(string.Empty, Normalizer.Normalize("Hizmet Alımı"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void Nothing_in_gives_an_empty_string_out_rather_than_an_exception(string? input)
    {
        Assert.Equal(string.Empty, Normalizer.Normalize(input));
    }

    [Fact]
    public void The_suffix_list_is_data_and_is_ordered_longest_first()
    {
        // "hizmet alımı" has to be tried before "alımı", or the longer phrase
        // never matches.
        var lengths = Rules.Suffixes.Select(suffix => suffix.Length).ToArray();

        Assert.NotEmpty(lengths);
        Assert.Equal(lengths.OrderByDescending(length => length), lengths);
    }
}
