using ProcurementRadar.Domain.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using ProcurementRadar.Collector;

namespace ProcurementRadar.Parsers.Tests;

/// <summary>
/// Against the real result announcement EKAP returned for 2026/1166872 - a web
/// HR system bought by Doğa Koruma ve Milli Parklar, captured with the ekap-probe
/// command. Hand-written html would only test what I assumed the page looks like.
/// </summary>
public sealed class AwardParserTests
{
    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory, "fixtures", "ekap_award", "2026-1166872-sonuc.json");

    private static readonly AwardLabels Labels = AwardLabels.Load(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config", "tr-award-labels.json")));

    private static readonly TurkishDatePatterns DatePatterns = TurkishDatePatterns.Load(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config", "tr-date-patterns.json")));

    private static AwardParser Parser() => new(
        new Fixed<AwardLabels>(Labels),
        new TurkishDateParser(new Fixed<TurkishDatePatterns>(DatePatterns), NullLogger<TurkishDateParser>.Instance));

    private static string Announcement() => EkapAnnouncementEnricher.PlainText(File.ReadAllText(FixturePath));

    /// <summary>
    /// The same announcement EKAP served on 6 September 2026 for 2026/1360913,
    /// after it renamed the contract row.
    /// </summary>
    private static readonly string RenamedFixturePath = Path.Combine(
        AppContext.BaseDirectory, "fixtures", "ekap_award", "2026-1360913-sonuc.json");

    private static string RenamedAnnouncement() =>
        EkapAnnouncementEnricher.PlainText(File.ReadAllText(RenamedFixturePath));

    [Fact]
    public void The_settings_table_can_still_be_holding_the_older_single_label()
    {
        // D-16: the seeder fills a key it does not find and leaves the ones it
        // does. So on every machine that has run before, the stored award_labels
        // is the one-string version until somebody pushes a new one - and a
        // loader that refused it would turn the deploy that fixes this into the
        // deploy that will not start.
        var older = AwardLabels.Load("""
            {
              "result_marker": "SONUÇ İLANI",
              "contract_section": "Sözleşmenin",
              "winner_label": "Yüklenici",
              "amount_label": "Bedeli",
              "date_label": "Tarihi",
              "bid_count_label": "Toplam Teklif Sayısı",
              "valid_bid_count_label": "Toplam Geçerli Teklif Sayısı",
              "item_letters": "abcçdefgğhıijklmnoöprsştuüvyz"
            }
            """);

        Assert.Equal(["Yüklenici"], older.WinnerLabels);

        var parser = new AwardParser(
            new Fixed<AwardLabels>(older),
            new TurkishDateParser(new Fixed<TurkishDatePatterns>(DatePatterns), NullLogger<TurkishDateParser>.Instance));

        // It reads the July announcement, which is what it was written for, and
        // reads no winner out of the renamed one. That second half is the bug
        // this whole change is about, stated as a test.
        Assert.Equal(
            "PROMETUS BİLGİSAYAR SİSTEMLERİ SANAYİ VE TİCARET LİMİTED ŞİRKETİ",
            parser.Parse(Announcement())!.WinnerName);

        Assert.Null(parser.Parse(RenamedAnnouncement())!.WinnerName);
    }

    [Fact]
    public void The_row_EKAP_renamed_is_still_read()
    {
        // "d) Yüklenici" became "d) Yüklenicisi". The colon has to follow the
        // label immediately - that is what keeps "Yüklenicinin uyruğu" out - so
        // the old single label stopped matching entirely and every award since
        // the rename was written with a null winner. Nothing failed: the sweeper
        // reads a null winner as "no result yet", so 44 tenders were asked about
        // 186 times and 21 of them used up all six attempts on an answer that was
        // sitting in the text.
        var award = Parser().Parse(RenamedAnnouncement());

        Assert.NotNull(award);
        Assert.Equal(
            "DİJİTAL ARŞİV BELGE BİLGİ TEKNOLOJİLERİ SANAYİ VE TİCARET ANONİM ŞİRKETİ",
            award.WinnerName);
    }

    [Fact]
    public void The_rest_of_the_renamed_announcement_reads_the_same_way()
    {
        // The other four labels were checked against this announcement and all
        // of them still match, so the rename was one row and not a redesign.
        var award = Parser().Parse(RenamedAnnouncement());

        Assert.NotNull(award);
        Assert.Equal(800000m, award.ContractAmount);
        Assert.Equal(2, award.BidCount);
    }

    [Fact]
    public void The_winner_is_the_firm_and_not_the_row_below_it()
    {
        var award = Parser().Parse(Announcement());

        // "d) Yüklenici" is followed by "e) Yüklenicinin uyruğu" and
        // "f) Yüklenicinin adresi", which both begin with the same word. A looser
        // match returns Türkiye, or the whole rest of the announcement.
        Assert.NotNull(award);
        Assert.Equal("PROMETUS BİLGİSAYAR SİSTEMLERİ SANAYİ VE TİCARET LİMİTED ŞİRKETİ", award.WinnerName);
    }

    [Fact]
    public void The_amount_is_what_was_signed_not_what_was_estimated()
    {
        var award = Parser().Parse(Announcement());

        // The same announcement carries "Yaklaşık Maliyeti : 3.405.000,00 TRY"
        // higher up. Reading that one instead would make every award in the
        // archive agree with its estimate, which is the one thing this table
        // exists to disprove.
        Assert.Equal(3_300_000m, award!.ContractAmount);
    }

    [Fact]
    public void The_bid_count_is_the_valid_one()
    {
        var award = Parser().Parse(Announcement());

        Assert.Equal(1, award!.BidCount);
    }

    [Fact]
    public void The_date_is_the_contract_date_not_the_tender_date()
    {
        var award = Parser().Parse(Announcement());

        // Contract 16.07.2026, tender 30.06.2026, both labelled "Tarihi", and
        // the wrong one is the one that appears first.
        //
        // The time is the end of the working day in Istanbul - 17:00, so 14:00
        // here in July - because a bare date means that everywhere else in this
        // system, where it is a deadline and guessing late is the dangerous
        // direction. Reused here rather than given a second meaning: the day is
        // what an award date is for, and one parser that treats a date one way
        // is worth more than two that disagree.
        Assert.Equal(
            new DateTimeOffset(2026, 7, 16, 14, 0, 0, TimeSpan.Zero),
            award!.AwardedAt);
    }

    [Fact]
    public void An_announcement_that_is_not_a_result_yields_nothing()
    {
        // What comes back for everything still open: a tender announcement. It
        // has an İKN, an authority and a date, so a parser that took the first
        // match of each would happily record an award for a tender nobody has
        // bid on yet.
        var tenderAnnouncement = Parser().Parse(
            "İHALE İLANI Yazılım Alınacaktır İhale kayıt numarası : 2026/1234567 "
            + "1- İdarenin a) Tarihi : 30.06.2026");

        Assert.Null(tenderAnnouncement);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("{}")]
    public void Nothing_readable_is_not_an_exception(string? text)
    {
        Assert.Null(Parser().Parse(text));
    }
}
