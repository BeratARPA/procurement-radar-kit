using Microsoft.Extensions.Logging.Abstractions;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Settings;
using ProcurementRadar.Parsers;

namespace ProcurementRadar.Parsers.Tests;

/// <summary>
/// Telling "there is nothing on the page today" apart from "the page changed
/// and we can no longer read it".
/// </summary>
/// <remarks>
/// Both look identical in the database - HTTP 200, zero notices, a content hash
/// that moved - and spec.md Bölüm 11 raises "parser broken" for the pair.
/// Kahramankazan raised it every day from 16 to 18 August with correct
/// selectors: the municipality had no open tender and the site served the list
/// container with nothing inside it.
///
/// The two fixtures are that source's own archived bodies (D-09), one from a
/// fetch that read a tender and one from a fetch an hour later that read none.
/// They differ by a single li in 105 KB, which is the whole point: everything
/// the database can see about them is the same.
/// </remarks>
public sealed class EmptyListTests
{
    private const string RowSelector = "div.common-list ul li";

    private static GenericTableAdapter Adapter() => new(
        new UnusableFetcher(),
        new TurkishDateParser(
            new Fixed<TurkishDatePatterns>(TurkishDatePatterns.Load(
                File.ReadAllText(Path.Combine(
                    AppContext.BaseDirectory, "config", "tr-date-patterns.json")))),
            NullLogger<TurkishDateParser>.Instance),
        NullLogger<GenericTableAdapter>.Instance);

    private static Source SourceWith(string rowSelector) => new()
    {
        Id = 19,
        AuthorityName = "Kahramankazan Belediyesi",
        AuthorityType = AuthorityType.Municipality,
        Province = "Ankara",
        Url = "https://kahramankazan.bel.tr/ihaleler",
        AdapterKey = GenericTableAdapter.AdapterKey,
        AdapterConfig = $$"""
            {
              "base_url": "https://kahramankazan.bel.tr",
              "row_selector": "{{rowSelector}}",
              "link_selector": "a.common-list__wrapper",
              "title_selector": "h3.title"
            }
            """
    };

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "fixtures", GenericTableAdapter.AdapterKey, name));

    /// <summary>The archived page from the fetch that read a tender off it.</summary>
    private static string WithATender => Fixture("kahramankazan-with-tender.html");

    /// <summary>The archived page from a fetch an hour later. Same 105 KB, one li fewer.</summary>
    private static string WithNothing => Fixture("kahramankazan-empty-list.html");

    /// <summary>
    /// What a redesign looks like: the listing is gone, not emptied.
    /// </summary>
    /// <remarks>
    /// The one hand-written body here, and it has to be: no archived page shows
    /// this site redesigned, because it has not been. What is under test is the
    /// container check itself, and the case that must never answer "empty" is
    /// the one with no container at all.
    /// </remarks>
    private const string Redesigned = """
        <html><body><div class="wrapper"><section class="tenders">
          <article><h2>DÜKKAN İŞLETMECİLİĞİ</h2></article>
        </section></div></body></html>
        """;

    [Fact]
    public void A_page_with_a_tender_on_it_is_not_an_empty_list()
    {
        var source = SourceWith(RowSelector);

        Assert.Single(Adapter().Parse(WithATender, source));
        Assert.False(Adapter().ListWasEmpty(WithATender, source));
    }

    [Fact]
    public void The_container_present_and_empty_reads_as_an_empty_list()
    {
        var source = SourceWith(RowSelector);

        Assert.Empty(Adapter().Parse(WithNothing, source));
        Assert.True(Adapter().ListWasEmpty(WithNothing, source));
    }

    [Fact]
    public void A_page_that_lost_its_listing_still_raises_the_alarm()
    {
        // The half that matters more. If this ever answered true, a site could
        // be redesigned and the one rule that catches it within hours would go
        // quiet - and spec.md Bölüm 11 opens with what that costs.
        var source = SourceWith(RowSelector);

        Assert.Empty(Adapter().Parse(Redesigned, source));
        Assert.False(Adapter().ListWasEmpty(Redesigned, source));
    }

    [Fact]
    public void A_selector_with_no_container_to_test_answers_unknown()
    {
        // "tr" has no parent step to fall back on, so there is nothing to check
        // and the honest answer is the noisy one.
        var source = SourceWith("li");

        Assert.False(Adapter().ListWasEmpty(WithNothing, source));
    }

    [Fact]
    public void An_unusable_config_answers_unknown_rather_than_throwing()
    {
        var source = SourceWith(RowSelector) with { AdapterConfig = "{}" };

        Assert.False(Adapter().ListWasEmpty(WithNothing, source));
    }

    private sealed class UnusableFetcher : Domain.Fetching.IHttpFetcher
    {
        public Task<Domain.Fetching.FetchResult> FetchAsync(
            Domain.Fetching.FetchRequest request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("These tests read a saved page; they do not fetch.");
    }
}
