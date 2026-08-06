using Microsoft.Extensions.Logging.Abstractions;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Fetching;
using ProcurementRadar.Parsers;

namespace ProcurementRadar.Parsers.Tests;

/// <summary>
/// Runs <see cref="EkapDirectProcurementAdapter.Parse"/> over a response captured
/// from EKAP's doğrudan temin search.
/// </summary>
public sealed class EkapDirectProcurementAdapterTests
{
    private static readonly TurkishDatePatterns DatePatterns = TurkishDatePatterns.Load(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config", "tr-date-patterns.json")));

    private static string Body => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "fixtures",
        EkapDirectProcurementAdapter.AdapterKey,
        "ankara-yazilim.json"));

    private static EkapDirectProcurementAdapter Adapter() => new(
        new UnusableSearchClient(),
        new TurkishDateParser(DatePatterns, NullLogger<TurkishDateParser>.Instance),
        NullLogger<EkapDirectProcurementAdapter>.Instance);

    private static Source SourceWith(string config) => new()
    {
        Id = 2,
        AuthorityName = "EKAP",
        AuthorityType = AuthorityType.Ekap,
        Province = "Ankara",
        Url = "https://ekapv2.kik.gov.tr/ekap-dt/search",
        AdapterKey = EkapDirectProcurementAdapter.AdapterKey,
        AdapterConfig = config
    };

    [Fact]
    public void The_captured_response_reads_as_a_notice()
    {
        var notice = Assert.Single(Adapter().Parse(Body, SourceWith("""{"search_text":"yazılım"}""")));

        Assert.StartsWith("DSİ Genel Müdürlüğü Chiller", notice.Title, StringComparison.Ordinal);
        Assert.Equal("DEVLET SU İŞLERİ GENEL MÜDÜRLÜĞÜ DESTEK HİZMETLERİ DAİRESİ BAŞKANLIĞI", notice.AuthorityName);
    }

    [Fact]
    public void The_deadline_is_taken_from_the_offset_in_the_text_and_not_from_this_machine()
    {
        // EKAP states this one as ISO 8601 with an offset - the only source in the
        // project that does. Read with RoundtripKind so the offset decides; a
        // server in another zone must produce the same instant (D-08).
        var notice = Adapter().Parse(Body, SourceWith("""{"search_text":"yazılım"}"""))[0];

        Assert.Equal(
            DateTimeOffset.Parse("2026-06-29T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            notice.BidDeadlineAt);

        Assert.Equal(TimeSpan.Zero, notice.BidDeadlineAt!.Value.Offset);
    }

    [Fact]
    public void The_doğrudan_temin_number_is_carried_as_the_identity()
    {
        // The same purchase can appear here and on the authority's own page. This
        // number is the state's own, so it decides (spec Bölüm 8).
        Assert.Equal(
            "26DT5000142",
            Adapter().Parse(Body, SourceWith("""{"search_text":"yazılım"}"""))[0].Ikn);
    }

    [Fact]
    public void The_article_it_was_let_under_survives_into_the_description()
    {
        // 22/d is the whole reason this project exists; losing it would leave the
        // scorer unable to tell a doğrudan temin from anything else.
        Assert.Contains(
            "22/d",
            Adapter().Parse(Body, SourceWith("""{"search_text":"yazılım"}"""))[0].Description!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_province_filter_reads_the_nested_province()
    {
        Assert.Single(Adapter().Parse(Body, SourceWith("""{"search_text":"yazılım","province":"ANKARA"}""")));
        Assert.Empty(Adapter().Parse(Body, SourceWith("""{"search_text":"yazılım","province":"İSTANBUL"}""")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("<html>Bakım</html>")]
    public void An_unexpected_body_comes_back_empty_rather_than_throwing(string body)
        => Assert.Empty(Adapter().Parse(body, SourceWith("""{"search_text":"yazılım"}""")));

    private sealed class UnusableSearchClient : IEkapSearchClient
    {
        public Task<FetchResult> SearchAsync(EkapSearchRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Parse must not search.");
    }
}
