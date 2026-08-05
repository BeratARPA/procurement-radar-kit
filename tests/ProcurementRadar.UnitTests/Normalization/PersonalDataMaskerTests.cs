using ProcurementRadar.Domain.Normalization;

namespace ProcurementRadar.UnitTests.Normalization;

public sealed class PersonalDataMaskerTests
{
    private static readonly PersonalDataMasker Masker = new(PersonalDataRules.Load(
        File.ReadAllText(Path.Combine(
            RepositoryRoot.Find().FullName, "config", "tr-personal-data.json"))));

    [Theory]
    [InlineData("Tel: 0312 123 45 67")]
    [InlineData("Telefon: (0312) 1234567")]
    [InlineData("GSM: 0532 123 45 67")]
    [InlineData("+90 312 123 45 67 numaralı telefondan")]
    public void Phone_numbers_do_not_survive(string text)
    {
        var masked = Masker.MaskText(text)!;

        Assert.DoesNotContain("123 45 67", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("1234567", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_addresses_do_not_survive()
    {
        var masked = Masker.MaskText("Sorular için ahmet.yilmaz@cankaya.bel.tr adresine yazınız.")!;

        Assert.DoesNotContain("@", masked, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("İrtibat: Ahmet Yılmaz")]
    [InlineData("irtibat: Ahmet Yılmaz")]
    [InlineData("IRTIBAT: Ahmet Yılmaz")]
    [InlineData("Yetkili Kişi: Ahmet Yılmaz")]
    [InlineData("Bilgi için: Ahmet Yılmaz")]
    public void A_name_behind_a_contact_label_does_not_survive_whatever_its_casing(string text)
    {
        // The Turkish dotted capital İ is the trap here: under invariant
        // case-insensitive matching it is not the same letter as i, so a label
        // written the way a page actually writes it would slip through.
        Assert.DoesNotContain("Ahmet", Masker.MaskText(text)!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_part_of_a_description_that_is_about_the_work_survives()
    {
        var masked = Masker.MaskText(
            "Mevcut personel takip sistemine modül eklenmesi işi. İrtibat: Ahmet Yılmaz.")!;

        Assert.Contains("modül eklenmesi", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("Ahmet", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void An_IKN_is_not_mistaken_for_a_phone_number()
    {
        // It is the deduplication key. Masking it would split one tender into a
        // new notice on every pass.
        const string Text = "İhale Kayıt Numarası 2026/123456";

        Assert.Equal(Text, Masker.MaskText(Text));
    }

    [Theory]
    [InlineData("İhale tarihi 12.03.2026")]
    [InlineData("Son teklif: 12.03.2026 10:00")]
    [InlineData("Yaklaşık maliyet 180.000,00 TL")]
    public void Dates_and_amounts_are_not_mistaken_for_phone_numbers(string text)
    {
        Assert.Equal(text, Masker.MaskText(text));
    }

    [Fact]
    public void A_word_that_merely_ends_in_a_label_is_left_alone()
    {
        // "otel" ends in "tel"; without a word boundary the rest of the sentence
        // would be blanked.
        var masked = Masker.MaskText("Otel rezervasyon sistemi yazılımı alınacaktır")!;

        Assert.Contains("rezervasyon sistemi", masked, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_in_nothing_out(string? text)
    {
        Assert.Equal(text, Masker.MaskText(text));
    }
}
