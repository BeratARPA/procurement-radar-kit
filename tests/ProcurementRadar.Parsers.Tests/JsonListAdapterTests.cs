using Microsoft.Extensions.Logging.Abstractions;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Settings;

namespace ProcurementRadar.Parsers.Tests;

/// <summary>
/// Against the real responses from two municipal applications, captured with
/// watch-requests on 8 August 2026.
/// </summary>
/// <remarks>
/// Both pages had defeated every attempt to read them as html - one is a 1,716
/// byte React shell - and both hand over their tenders as json to anyone who
/// asks the right address. These fixtures are those answers, unedited.
/// </remarks>
public sealed class JsonListAdapterTests
{
    private static readonly TurkishDatePatterns DatePatterns = TurkishDatePatterns.Load(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config", "tr-date-patterns.json")));

    private static JsonListAdapter Adapter() => new(
        new UnusableFetcher(),
        new TurkishDateParser(new Fixed<TurkishDatePatterns>(DatePatterns), NullLogger<TurkishDateParser>.Instance),
        NullLogger<JsonListAdapter>.Instance);

    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "json_list", name));

    private static Source SourceWith(string config, string url) => new()
    {
        Id = 0,
        AuthorityName = "test",
        AuthorityType = AuthorityType.Municipality,
        Province = "Ankara",
        Url = url,
        AdapterKey = JsonListAdapter.AdapterKey,
        AdapterConfig = config
    };

    [Fact]
    public void A_nested_list_is_found_by_its_path()
    {
        // content.tenderListe - the array is two levels down, which is why the
        // path is configuration rather than a guess at the first array found.
        var notices = Adapter().Parse(
            Fixture("yenimahalle-tenders.json"),
            SourceWith(
                """
                {"list_path":"content.tenderListe","title_field":"tenderHeader",
                 "description_field":"tenderIntroText",
                 "link_template":"https://www.yenimahalle.bel.tr/IhaleDetay/{id}","id_field":"id"}
                """,
                "https://yenimahalle-web-api.alsft.com/api/Portal/GetActiveTenders"));

        Assert.NotEmpty(notices);
        Assert.All(notices, notice => Assert.NotEmpty(notice.Title));
    }

    [Fact]
    public void The_link_is_built_from_the_id()
    {
        // Recording every notice against the endpoint would give a person a page
        // of json to read.
        var notices = Adapter().Parse(
            Fixture("yenimahalle-tenders.json"),
            SourceWith(
                """
                {"list_path":"content.tenderListe","title_field":"tenderHeader",
                 "link_template":"https://www.yenimahalle.bel.tr/IhaleDetay/{id}","id_field":"id"}
                """,
                "https://yenimahalle-web-api.alsft.com/api/Portal/GetActiveTenders"));

        Assert.StartsWith(
            "https://www.yenimahalle.bel.tr/IhaleDetay/",
            notices[0].SourceUrl,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_at_the_top_level_works_too()
    {
        var notices = Adapter().Parse(
            Fixture("baskent-dogalgaz-sections.json"),
            SourceWith(
                """{"list_path":"sections","title_field":"title","description_field":"detail"}""",
                "https://www.baskentdogalgaz.com.tr/tr/page/Ihale-Duyurulari/127"));

        Assert.NotEmpty(notices);
        Assert.Contains(notices, notice => notice.Title.Contains("İHALE", StringComparison.Ordinal));
    }

    [Fact]
    public void A_path_that_is_not_there_returns_nothing_rather_than_throwing()
    {
        // What a changed application looks like. It has to be visible as zero
        // notices and a warning, not as an exception that stops the run.
        var notices = Adapter().Parse(
            Fixture("baskent-dogalgaz-sections.json"),
            SourceWith(
                """{"list_path":"content.whatever","title_field":"title"}""",
                "https://example.invalid/"));

        Assert.Empty(notices);
    }

    [Fact]
    public void Something_that_is_not_json_is_not_an_exception()
    {
        var notices = Adapter().Parse(
            "<html><body>bir sayfa</body></html>",
            SourceWith("""{"list_path":"sections","title_field":"title"}""", "https://example.invalid/"));

        Assert.Empty(notices);
    }

    private sealed class UnusableFetcher : Domain.Fetching.IHttpFetcher
    {
        public Task<Domain.Fetching.FetchResult> FetchAsync(
            Domain.Fetching.FetchRequest request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("Parse is a pure function.");
    }
}
