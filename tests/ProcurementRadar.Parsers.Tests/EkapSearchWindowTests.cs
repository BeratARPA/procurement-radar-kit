using ProcurementRadar.Parsers;

namespace ProcurementRadar.Parsers.Tests;

/// <summary>
/// Guards the two settings that decide which end of EKAP's results we see.
/// </summary>
/// <remarks>
/// Measured against the live site on 08.08.2026, and the numbers are the whole
/// reason these fields exist. A search for "otomasyon" matched 9425 tenders;
/// EKAP returned ten, sorted by tender date with the furthest future first, so
/// every row was dated between 02.09 and 07.09 - a month out. Nothing was
/// broken. A tender closing the following Tuesday was simply on a later page,
/// and there were 943 of them.
///
/// Switching on EKAP's own tender-date filter took the same search to 29 results
/// over 3 pages, and their page-size box offers 50. Together that is the whole
/// open set in one request instead of a distant tenth of it.
///
/// So these are not preferences. Losing the filter puts the closed tenders back;
/// losing the page size hides four fifths of what is left.
/// </remarks>
public sealed class EkapSearchWindowTests
{
    [Fact]
    public void The_filter_and_page_size_survive_a_round_trip()
    {
        var read = EkapAdapterConfig.TryRead(
            """
            {
              "search_text": "otomasyon",
              "filter_label": "Bugünden İtibaren",
              "page_size": 50
            }
            """,
            out var config,
            out var problem);

        Assert.True(read, problem);
        Assert.Equal("Bugünden İtibaren", config.FilterLabel);
        Assert.Equal(50, config.PageSize);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(50)]
    public void The_sizes_ekap_offers_are_accepted(int size)
    {
        EkapAdapterConfig.TryRead(
            $$"""{"search_text": "otomasyon", "page_size": {{size}}}""",
            out var config,
            out _);

        Assert.Equal(size, config.PageSize);
    }

    /// <summary>
    /// A size EKAP does not offer is refused rather than carried.
    /// </summary>
    /// <remarks>
    /// Their box is a dropdown with three entries, not a text field. Asking for a
    /// hundred would find no option to click, the click would time out, and the
    /// run would keep the ten it already had - while the config on screen said a
    /// hundred. A window that is a fifth of what the settings claim is the kind
    /// of quiet failure spec.md Bölüm 11 is written about.
    /// </remarks>
    [Theory]
    [InlineData(100)]
    [InlineData(25)]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_size_ekap_does_not_offer_is_refused(int size)
    {
        EkapAdapterConfig.TryRead(
            $$"""{"search_text": "otomasyon", "page_size": {{size}}}""",
            out var config,
            out _);

        Assert.Null(config.PageSize);
    }

    /// <summary>Both are optional, and their absence is the old behaviour.</summary>
    [Fact]
    public void A_config_without_them_still_reads()
    {
        var read = EkapAdapterConfig.TryRead(
            """{"search_text": "otomasyon"}""",
            out var config,
            out var problem);

        Assert.True(read, problem);
        Assert.Null(config.FilterLabel);
        Assert.Null(config.PageSize);
    }
}
