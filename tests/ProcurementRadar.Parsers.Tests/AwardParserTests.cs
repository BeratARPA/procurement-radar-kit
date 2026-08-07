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
        Labels,
        new TurkishDateParser(DatePatterns, NullLogger<TurkishDateParser>.Instance));

    private static string Announcement() => EkapAnnouncementEnricher.PlainText(File.ReadAllText(FixturePath));

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
        // The time is the end of that day in Istanbul (20:59 UTC in summer), not
        // midnight: a bare date means "up to and including" everywhere else in
        // this system, because everywhere else it is a deadline. Reused here
        // rather than given a second meaning - the day is what an award date is
        // for, and one parser that treats a date one way is worth more than two
        // that disagree.
        Assert.Equal(
            new DateTimeOffset(2026, 7, 16, 20, 59, 0, TimeSpan.Zero),
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
