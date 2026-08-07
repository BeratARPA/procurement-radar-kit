using ProcurementRadar.Collector;

namespace ProcurementRadar.UnitTests.Fetching;

/// <summary>
/// Which response belongs to our search.
/// </summary>
/// <remarks>
/// The page issues a search of its own while it is being prepared, gets an empty
/// list back, and the wait - armed before the term is typed, because the real
/// answer can otherwise arrive first - used to accept whichever came first.
///
/// It cost two "parser broken" alarms on a working source: the empty body was
/// archived as if it were the page, the parser was never called, and the health
/// rule correctly reported that a changed page yielded no notices. The rule was
/// right; the thing it was reporting on had not run.
/// </remarks>
public sealed class EkapSearchPayloadTests
{
    [Fact]
    public void The_application_searching_for_itself_is_not_our_search()
    {
        Assert.False(PlaywrightEkapSearchClient.Asks(
            """{"aramaKriteri":"","sayfa":1,"sayfaBoyutu":10}""",
            "yazılım"));
    }

    [Fact]
    public void Our_own_search_is_recognised()
    {
        Assert.True(PlaywrightEkapSearchClient.Asks(
            """{"aramaKriteri":"yazılım","sayfa":1,"sayfaBoyutu":10}""",
            "yazılım"));
    }

    [Fact]
    public void An_escaped_payload_is_recognised_too()
    {
        // Whether the page's serializer writes a Turkish letter straight out or
        // as an escape is its own business, and free to change in a release.
        Assert.True(PlaywrightEkapSearchClient.Asks(
            """{"aramaKriteri":"yaz\u0131l\u0131m"}""",
            "yazılım"));
    }

    [Fact]
    public void A_different_search_is_not_ours()
    {
        Assert.False(PlaywrightEkapSearchClient.Asks(
            """{"aramaKriteri":"asansör bakım"}""",
            "yazılım"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_request_with_no_body_is_not_ours(string? payload)
    {
        // A GET the application makes on the same address carries no payload,
        // and cannot be the search - which is a POST.
        Assert.False(PlaywrightEkapSearchClient.Asks(payload, "yazılım"));
    }
}
