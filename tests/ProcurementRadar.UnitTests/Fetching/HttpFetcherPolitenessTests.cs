using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Fetching;

namespace ProcurementRadar.UnitTests.Fetching;

public sealed class HttpFetcherPolitenessTests
{
    private static readonly HttpFetcherOptions Options = new()
    {
        ContactEmail = "team@example.org",
        // A long request timeout: the fake clock is advanced in bulk here, and a
        // realistic timeout would fire while a request is notionally in flight.
        RequestTimeout = TimeSpan.FromHours(1)
    };

    [Fact]
    public async Task Three_requests_to_one_host_are_at_least_five_seconds_apart()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));
        var handler = new StubHttpMessageHandler(
            time,
            (uri, _) => uri.AbsolutePath == "/robots.txt"
                ? StubHttpMessageHandler.NotFound()
                : StubHttpMessageHandler.Text(HttpStatusCode.OK, "<html>ilan</html>"));

        using var httpClient = new HttpClient(handler);
        using var fetcher = new HttpFetcher(
            httpClient,
            Options,
            time,
            NullLogger<HttpFetcher>.Instance,
            nextJitter: () => TimeSpan.Zero);

        var work = Task.Run(async () =>
        {
            for (var i = 0; i < 3; i++)
            {
                await fetcher.FetchAsync(new FetchRequest("https://example.gov.tr/ihaleler"), CancellationToken.None);
            }
        });

        await PumpAsync(time, work);

        var pageRequests = handler.RequestsTo("/ihaleler");
        Assert.Equal(3, pageRequests.Count);

        for (var i = 1; i < pageRequests.Count; i++)
        {
            var gap = pageRequests[i].At - pageRequests[i - 1].At;

            Assert.True(
                gap >= Options.MinimumHostInterval,
                $"Request {i + 1} came {gap} after the previous one, which is under the "
                + $"{Options.MinimumHostInterval} minimum.");
        }
    }

    [Fact]
    public async Task The_robots_lookup_also_waits_before_the_first_page_request()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));
        var handler = new StubHttpMessageHandler(
            time,
            (uri, _) => uri.AbsolutePath == "/robots.txt"
                ? StubHttpMessageHandler.Text(HttpStatusCode.OK, "User-agent: *\nDisallow: /admin", "text/plain")
                : StubHttpMessageHandler.Text(HttpStatusCode.OK, "<html/>"));

        using var httpClient = new HttpClient(handler);
        using var fetcher = new HttpFetcher(
            httpClient,
            Options,
            time,
            NullLogger<HttpFetcher>.Instance,
            nextJitter: () => TimeSpan.Zero);

        var work = Task.Run(() => fetcher.FetchAsync(
            new FetchRequest("https://example.gov.tr/ihaleler"),
            CancellationToken.None));

        await PumpAsync(time, work);

        var robots = Assert.Single(handler.RequestsTo("/robots.txt"));
        var page = Assert.Single(handler.RequestsTo("/ihaleler"));

        Assert.True(
            page.At - robots.At >= Options.MinimumHostInterval,
            "Reading robots.txt is a request to the same host and has to be spaced like any other.");
    }

    [Fact]
    public void Jitter_stays_inside_the_configured_band()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));
        using var gate = new HostPolitenessGate(time, Options);

        // Sampled rather than reasoned about: the jitter is random by design, and
        // what matters is that it never leaves the band CLAUDE.md fixes.
        for (var i = 0; i < 200; i++)
        {
            var wait = gate.WaitBefore(time.GetUtcNow());

            Assert.InRange(
                wait,
                Options.MinimumHostInterval,
                Options.MinimumHostInterval + Options.MaximumJitter);
        }
    }

    [Fact]
    public void A_host_that_has_never_been_asked_still_gets_a_little_jitter()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));
        using var gate = new HostPolitenessGate(time, Options);

        // No five second wait, because nothing has been asked of this host yet.
        // The jitter stays, so a restart does not fire every source at once.
        for (var i = 0; i < 50; i++)
        {
            Assert.InRange(gate.WaitBefore(lastRequestFinishedAt: null), TimeSpan.Zero, Options.MaximumJitter);
        }
    }

    [Fact]
    public async Task The_user_agent_carries_the_project_name_and_a_contact_address()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));
        var handler = new StubHttpMessageHandler(time, (_, _) => StubHttpMessageHandler.Text(HttpStatusCode.OK, "ok"));

        using var httpClient = new HttpClient(handler);
        using var fetcher = new HttpFetcher(
            httpClient,
            Options,
            time,
            NullLogger<HttpFetcher>.Instance,
            nextJitter: () => TimeSpan.Zero);

        var work = Task.Run(() => fetcher.FetchAsync(
            new FetchRequest("https://example.gov.tr/ihaleler"),
            CancellationToken.None));

        await PumpAsync(time, work);

        var userAgent = handler.Requests[0].Headers.UserAgent;

        Assert.Contains("ProcurementRadar/1.0", userAgent, StringComparison.Ordinal);
        Assert.Contains("team@example.org", userAgent, StringComparison.Ordinal);
    }

    /// <summary>
    /// Drives the fake clock forward until the work finishes. Advancing in small
    /// steps means a delay registered after the previous advance is still picked
    /// up on the next one, so the loop cannot race past a pending wait.
    /// </summary>
    private static async Task PumpAsync(FakeTimeProvider time, Task work)
    {
        for (var step = 0; step < 2000 && !work.IsCompleted; step++)
        {
            time.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(1);
        }

        Assert.True(work.IsCompleted, "The fetches never finished; the clock pump gave up.");
        await work;
    }
}
