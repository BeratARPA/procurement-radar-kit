using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Fetching;

namespace ProcurementRadar.UnitTests.Fetching;

/// <summary>
/// Behaviour that has nothing to do with timing. These run on the real clock
/// with the politeness delays turned off, so they stay fast; the spacing itself
/// is covered by <see cref="HttpFetcherPolitenessTests"/>.
/// </summary>
public sealed class HttpFetcherBehaviourTests
{
    private static HttpFetcherOptions Immediate => new()
    {
        ContactEmail = "team@example.org",
        MinimumHostInterval = TimeSpan.Zero,
        MaximumJitter = TimeSpan.Zero,
        RetryBaseDelay = TimeSpan.FromMilliseconds(1)
    };

    [Fact]
    public async Task A_304_answer_leaves_the_body_on_the_wire()
    {
        var handler = new StubHttpMessageHandler(
            TimeProvider.System,
            (uri, _) =>
            {
                if (uri.AbsolutePath == "/robots.txt")
                {
                    return StubHttpMessageHandler.NotFound();
                }

                // Reading this content throws. If the fetcher touches it, the
                // test fails with that message rather than passing quietly.
                var response = new HttpResponseMessage(HttpStatusCode.NotModified)
                {
                    Content = new UnreadableContent()
                };
                response.Headers.TryAddWithoutValidation("ETag", "\"abc\"");

                return response;
            });

        using var httpClient = new HttpClient(handler);
        using var fetcher = new HttpFetcher(httpClient, Immediate, TimeProvider.System, NullLogger<HttpFetcher>.Instance);

        var result = await fetcher.FetchAsync(
            new FetchRequest("https://example.gov.tr/ihaleler", ETag: "\"abc\""),
            CancellationToken.None);

        Assert.Equal(FetchOutcome.NotModified, result.Outcome);
        Assert.Equal(304, result.HttpStatus);
        Assert.Null(result.Body);
        Assert.Null(result.ContentHash);
    }

    [Fact]
    public async Task The_conditional_headers_are_sent_when_we_have_them()
    {
        var handler = new StubHttpMessageHandler(
            TimeProvider.System,
            (uri, _) => uri.AbsolutePath == "/robots.txt"
                ? StubHttpMessageHandler.NotFound()
                : new HttpResponseMessage(HttpStatusCode.NotModified));

        using var httpClient = new HttpClient(handler);
        using var fetcher = new HttpFetcher(httpClient, Immediate, TimeProvider.System, NullLogger<HttpFetcher>.Instance);

        await fetcher.FetchAsync(
            new FetchRequest(
                "https://example.gov.tr/ihaleler",
                ETag: "\"v7\"",
                LastModified: "Wed, 04 Mar 2026 10:00:00 GMT"),
            CancellationToken.None);

        var page = Assert.Single(handler.RequestsTo("/ihaleler"));

        Assert.Equal("\"v7\"", page.Headers.IfNoneMatch);
        Assert.Equal("Wed, 04 Mar 2026 10:00:00 GMT", page.Headers.IfModifiedSince);
    }

    [Fact]
    public async Task A_disallowed_path_is_never_requested()
    {
        var handler = new StubHttpMessageHandler(
            TimeProvider.System,
            (uri, _) => uri.AbsolutePath == "/robots.txt"
                ? StubHttpMessageHandler.Text(
                    HttpStatusCode.OK,
                    "User-agent: *\nDisallow: /ihale\n",
                    "text/plain")
                : StubHttpMessageHandler.Text(HttpStatusCode.OK, "<html/>"));

        using var httpClient = new HttpClient(handler);
        using var fetcher = new HttpFetcher(httpClient, Immediate, TimeProvider.System, NullLogger<HttpFetcher>.Instance);

        var result = await fetcher.FetchAsync(
            new FetchRequest("https://example.gov.tr/ihale/liste"),
            CancellationToken.None);

        Assert.Equal(FetchOutcome.SkippedByRobots, result.Outcome);
        Assert.Empty(handler.RequestsTo("/ihale/liste"));
        Assert.Single(handler.RequestsTo("/robots.txt"));
    }

    [Fact]
    public async Task Robots_is_read_once_and_then_reused()
    {
        var handler = new StubHttpMessageHandler(
            TimeProvider.System,
            (uri, _) => uri.AbsolutePath == "/robots.txt"
                ? StubHttpMessageHandler.Text(HttpStatusCode.OK, "User-agent: *\nDisallow: /admin", "text/plain")
                : StubHttpMessageHandler.Text(HttpStatusCode.OK, "<html/>"));

        using var httpClient = new HttpClient(handler);
        using var fetcher = new HttpFetcher(httpClient, Immediate, TimeProvider.System, NullLogger<HttpFetcher>.Instance);

        for (var i = 0; i < 4; i++)
        {
            await fetcher.FetchAsync(new FetchRequest("https://example.gov.tr/ihaleler"), CancellationToken.None);
        }

        Assert.Single(handler.RequestsTo("/robots.txt"));
        Assert.Equal(4, handler.RequestsTo("/ihaleler").Count);
    }

    [Fact]
    public async Task A_robots_file_we_cannot_read_closes_the_host()
    {
        var handler = new StubHttpMessageHandler(
            TimeProvider.System,
            (uri, _) => uri.AbsolutePath == "/robots.txt"
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : StubHttpMessageHandler.Text(HttpStatusCode.OK, "<html/>"));

        using var httpClient = new HttpClient(handler);
        using var fetcher = new HttpFetcher(httpClient, Immediate, TimeProvider.System, NullLogger<HttpFetcher>.Instance);

        var result = await fetcher.FetchAsync(
            new FetchRequest("https://example.gov.tr/ihaleler"),
            CancellationToken.None);

        // A server that cannot serve its own robots.txt is the last one that
        // should be crawled on a guess.
        Assert.Equal(FetchOutcome.SkippedByRobots, result.Outcome);
        Assert.Empty(handler.RequestsTo("/ihaleler"));
    }

    [Fact]
    public async Task A_server_error_is_tried_three_times_in_total()
    {
        var handler = new StubHttpMessageHandler(
            TimeProvider.System,
            (uri, _) => uri.AbsolutePath == "/robots.txt"
                ? StubHttpMessageHandler.NotFound()
                : new HttpResponseMessage(HttpStatusCode.BadGateway));

        using var httpClient = new HttpClient(handler);
        using var fetcher = new HttpFetcher(httpClient, Immediate, TimeProvider.System, NullLogger<HttpFetcher>.Instance);

        var result = await fetcher.FetchAsync(
            new FetchRequest("https://example.gov.tr/ihaleler"),
            CancellationToken.None);

        Assert.Equal(FetchOutcome.Failed, result.Outcome);
        Assert.Equal(502, result.HttpStatus);
        Assert.Equal(3, handler.RequestsTo("/ihaleler").Count);
    }

    [Fact]
    public async Task A_404_is_not_retried()
    {
        var handler = new StubHttpMessageHandler(
            TimeProvider.System,
            (uri, _) => uri.AbsolutePath == "/robots.txt"
                ? StubHttpMessageHandler.NotFound()
                : StubHttpMessageHandler.NotFound());

        using var httpClient = new HttpClient(handler);
        using var fetcher = new HttpFetcher(httpClient, Immediate, TimeProvider.System, NullLogger<HttpFetcher>.Instance);

        var result = await fetcher.FetchAsync(
            new FetchRequest("https://example.gov.tr/yok"),
            CancellationToken.None);

        Assert.Equal(FetchOutcome.Failed, result.Outcome);
        // A missing page will still be missing five seconds from now. Retrying it
        // only spends politeness budget.
        Assert.Single(handler.RequestsTo("/yok"));
    }

    [Fact]
    public async Task A_fetched_body_is_hashed_over_the_bytes_that_arrived()
    {
        const string Body = "<html><body>Yazılım Bakım Hizmeti Alımı</body></html>";

        var handler = new StubHttpMessageHandler(
            TimeProvider.System,
            (uri, _) => uri.AbsolutePath == "/robots.txt"
                ? StubHttpMessageHandler.NotFound()
                : StubHttpMessageHandler.Text(HttpStatusCode.OK, Body));

        using var httpClient = new HttpClient(handler);
        using var fetcher = new HttpFetcher(httpClient, Immediate, TimeProvider.System, NullLogger<HttpFetcher>.Instance);

        var result = await fetcher.FetchAsync(
            new FetchRequest("https://example.gov.tr/ihaleler"),
            CancellationToken.None);

        var expected = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Body))).ToLowerInvariant();

        Assert.Equal(FetchOutcome.Fetched, result.Outcome);
        Assert.Equal(Body, result.Body);
        Assert.Equal(expected, result.ContentHash);
    }

    [Fact]
    public async Task A_url_that_is_not_http_is_refused_without_a_request()
    {
        var handler = new StubHttpMessageHandler(
            TimeProvider.System,
            (_, _) => StubHttpMessageHandler.Text(HttpStatusCode.OK, "ok"));

        using var httpClient = new HttpClient(handler);
        using var fetcher = new HttpFetcher(httpClient, Immediate, TimeProvider.System, NullLogger<HttpFetcher>.Instance);

        var result = await fetcher.FetchAsync(new FetchRequest("file:///etc/passwd"), CancellationToken.None);

        Assert.Equal(FetchOutcome.Failed, result.Outcome);
        Assert.Empty(handler.Requests);
    }
}
