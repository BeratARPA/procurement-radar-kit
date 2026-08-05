using ProcurementRadar.Domain.Normalization;

namespace ProcurementRadar.UnitTests.Normalization;

public sealed class ExternalKeyBuilderTests
{
    private static readonly ExternalKeyBuilder Builder =
        new(new NoticeNormalizer(NoticeNormalizerTests.Rules));

    private static readonly DateTimeOffset Deadline =
        new(2026, 3, 14, 11, 0, 0, TimeSpan.Zero); // 14:00 in Istanbul

    [Fact]
    public void The_same_notice_written_two_ways_produces_one_key()
    {
        // What EKAP prints, and what the authority puts on its own site.
        var fromEkap = Builder.Build(
            ikn: null,
            authorityName: "ÇANKAYA BELEDİYESİ",
            title: "Yazılım Bakım ve Destek Hizmeti Alımı",
            Deadline);

        var fromAuthority = Builder.Build(
            ikn: null,
            authorityName: "Çankaya Belediyesi",
            title: "Yazılım Bakım ve Destek",
            Deadline);

        Assert.Equal(fromEkap, fromAuthority);
    }

    [Fact]
    public void Punctuation_and_spacing_do_not_split_a_notice_in_two()
    {
        Assert.Equal(
            Builder.Build(null, "Ankara Üniversitesi", "Web Sitesi / Portal Yenileme İşi", Deadline),
            Builder.Build(null, "Ankara Universitesi", "Web sitesi - portal yenileme", Deadline));
    }

    [Fact]
    public void A_different_title_is_a_different_notice()
    {
        Assert.NotEqual(
            Builder.Build(null, "Çankaya Belediyesi", "Yazılım Bakım Hizmeti", Deadline),
            Builder.Build(null, "Çankaya Belediyesi", "Mobil Uygulama Geliştirme", Deadline));
    }

    [Fact]
    public void A_different_authority_is_a_different_notice()
    {
        Assert.NotEqual(
            Builder.Build(null, "Çankaya Belediyesi", "Yazılım Bakım Hizmeti", Deadline),
            Builder.Build(null, "Keçiören Belediyesi", "Yazılım Bakım Hizmeti", Deadline));
    }

    [Fact]
    public void A_different_deadline_day_is_a_different_notice()
    {
        Assert.NotEqual(
            Builder.Build(null, "Çankaya Belediyesi", "Yazılım Bakım Hizmeti", Deadline),
            Builder.Build(null, "Çankaya Belediyesi", "Yazılım Bakım Hizmeti", Deadline.AddDays(1)));
    }

    [Fact]
    public void The_day_is_the_one_the_page_printed_not_the_utc_one()
    {
        // 00:30 on 15 March in Istanbul is still 14 March in UTC. Both sources
        // print the 15th, so both have to key on the 15th.
        var justAfterMidnightIstanbul = new DateTimeOffset(2026, 3, 15, 0, 30, 0, TimeSpan.FromHours(3));
        var sameDayLater = new DateTimeOffset(2026, 3, 15, 23, 59, 0, TimeSpan.FromHours(3));

        Assert.Equal(
            Builder.Build(null, "Çankaya Belediyesi", "Yazılım Bakım", justAfterMidnightIstanbul),
            Builder.Build(null, "Çankaya Belediyesi", "Yazılım Bakım", sameDayLater));
    }

    [Fact]
    public void A_time_of_day_does_not_split_a_notice()
    {
        // One source gives 14:00, the other gives no time at all and falls back
        // to 23:59. Same day, same notice.
        Assert.Equal(
            Builder.Build(null, "Çankaya Belediyesi", "Yazılım Bakım", Deadline),
            Builder.Build(null, "Çankaya Belediyesi", "Yazılım Bakım",
                new DateTimeOffset(2026, 3, 14, 23, 59, 0, TimeSpan.FromHours(3))));
    }

    [Fact]
    public void An_IKN_is_used_as_the_key_directly()
    {
        Assert.Equal("2026/123456", Builder.Build("2026/123456", "Çankaya Belediyesi", "Herhangi bir başlık", Deadline));
    }

    [Fact]
    public void The_same_IKN_wins_over_titles_that_do_not_match()
    {
        // The state's own identifier settles it. Two sites describing tender
        // 2026/123456 differently are still describing one tender.
        Assert.Equal(
            Builder.Build("2026/123456", "Çankaya Belediyesi", "Yazılım Bakım Hizmeti Alımı", Deadline),
            Builder.Build(" 2026/123456 ", "Ankara Büyükşehir", "Bilgi Sistemi Güncelleme", Deadline.AddDays(9)));
    }

    [Fact]
    public void A_notice_with_no_deadline_still_gets_a_stable_key()
    {
        var first = Builder.Build(null, "Çankaya Belediyesi", "Yazılım Bakım", null);
        var second = Builder.Build(null, "Çankaya Belediyesi", "Yazılım Bakım Hizmeti Alımı", null);

        Assert.Equal(first, second);
        Assert.NotEqual(
            first,
            Builder.Build(null, "Çankaya Belediyesi", "Yazılım Bakım", Deadline));
    }

    [Fact]
    public void A_computed_key_is_a_sha256_hex_digest()
    {
        var key = Builder.Build(null, "Çankaya Belediyesi", "Yazılım Bakım", Deadline);

        Assert.Equal(64, key.Length);
        Assert.All(key, character => Assert.True(char.IsAsciiHexDigitLower(character)));
    }
}
