using Microsoft.Extensions.Logging.Abstractions;
using ProcurementRadar.Domain.Adapters;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Fetching;
using ProcurementRadar.Parsers;

namespace ProcurementRadar.Parsers.Tests;

/// <summary>
/// Runs <see cref="EkapAdapter.Parse"/> over a response captured from EKAP's own
/// search api.
/// </summary>
/// <remarks>
/// spec.md Bölüm 13 calls EKAP the most fragile component and says to keep the
/// tests tightest here. This is what that costs: the browser half is one method,
/// and everything that decides what ends up in the database is a pure function
/// over a saved response, tested without a browser at all.
/// </remarks>
public sealed class EkapAdapterTests
{
    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory, "fixtures", EkapAdapter.AdapterKey, "ankara-yazilim.json");

    private static readonly TurkishDatePatterns DatePatterns = TurkishDatePatterns.Load(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config", "tr-date-patterns.json")));

    private static string Body => File.ReadAllText(FixturePath);

    private static EkapAdapter Adapter() => new(
        new UnusableSearchClient(),
        new TurkishDateParser(DatePatterns, NullLogger<TurkishDateParser>.Instance),
        NullLogger<EkapAdapter>.Instance);

    private static Source SourceWith(string config) => new()
    {
        Id = 1,
        AuthorityName = "EKAP",
        AuthorityType = AuthorityType.Ekap,
        Province = "Ankara",
        Url = "https://ekapv2.kik.gov.tr/ekap/search",
        AdapterKey = EkapAdapter.AdapterKey,
        AdapterConfig = config
    };

    [Fact]
    public void The_captured_response_reads_as_notices()
    {
        var notices = Adapter().Parse(Body, SourceWith("""{"search_text":"yazılım"}"""));

        Assert.Equal(5, notices.Count);

        var first = notices[0];

        Assert.Equal("Siber Güvenlik Hizmeti Alım İşi", first.Title);
        Assert.Equal("BİLGİ İŞLEM DAİRE BAŞKANLIĞI", first.AuthorityName);

        // 23.03.2027 14:00 Europe/Istanbul. The clock matters more here than
        // anywhere: EKAP is the one source that states the hour outright.
        Assert.Equal(
            DateTimeOffset.Parse("2027-03-23T11:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            first.BidDeadlineAt);
    }

    [Fact]
    public void The_ikn_comes_through_because_deduplication_turns_on_it()
    {
        // The same notice appears on the authority's own page and on EKAP. Where
        // the state's own number is present it is the identity, and nothing
        // computed from a title can beat it (spec Bölüm 8).
        var notices = Adapter().Parse(Body, SourceWith("""{"search_text":"yazılım"}"""));

        Assert.Equal("2026/271215", notices[0].Ikn);
        Assert.All(notices, notice => Assert.False(string.IsNullOrWhiteSpace(notice.Ikn)));
    }

    [Fact]
    public void A_province_filter_keeps_only_that_province()
    {
        var notices = Adapter().Parse(
            Body,
            SourceWith("""{"search_text":"yazılım","province":"ANKARA"}"""));

        // Three of the five captured rows are Ankara; Mardin and Edirne go.
        Assert.Equal(3, notices.Count);
        Assert.All(notices, notice => Assert.Contains("ANKARA", notice.Description!, StringComparison.Ordinal));
    }

    [Fact]
    public void A_status_filter_drops_the_cancelled_ones()
    {
        // A cancelled tender is not work. It is noise carrying a deadline, which
        // is the worst kind: it looks urgent.
        var notices = Adapter().Parse(
            Body,
            SourceWith("""{"search_text":"yazılım","statuses":["İhale İlanı Yayımlanmış"]}"""));

        Assert.Equal(4, notices.Count);
        Assert.DoesNotContain(notices, notice => notice.Title.StartsWith("Siber", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("""{"list":null}""")]
    [InlineData("<html><body>Bakım</body></html>")]
    public void A_response_that_is_not_the_search_result_comes_back_empty_rather_than_throwing(string body)
    {
        // EKAP will change its shape, and the day it does this must produce zero
        // notices with an error in the log rather than an exception that stops
        // the run for every other source. T-15 turns the zero into the alarm.
        Assert.Empty(Adapter().Parse(body, SourceWith("""{"search_text":"yazılım"}""")));
    }

    [Fact]
    public void A_source_whose_config_is_unusable_parses_to_nothing_and_says_so()
    {
        Assert.Empty(Adapter().Parse(Body, SourceWith("""{"search_text":"TODO"}""")));
        Assert.Empty(Adapter().Parse(Body, SourceWith("{}")));
    }

    /// <summary>Parse must never reach the network - this would throw if it did.</summary>
    private sealed class UnusableSearchClient : IEkapSearchClient
    {
        public Task<FetchResult> SearchAsync(EkapSearchRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Parse must not search.");

        public Task<FetchResult> FetchAnnouncementAsync(string ikn, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Parse must not fetch an announcement.");
    }
}
