using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Fetching;

namespace ProcurementRadar.UnitTests.Fetching;

/// <summary>
/// Telling "this name will not resolve" apart from "this machine is off the
/// network".
/// </summary>
/// <remarks>
/// Both arrive as the same exception, and the log had been carrying the
/// difference for weeks: 1672 of 1769 fetch failures over one stretch were name
/// resolution, and they fell in twelve daytime windows where unrelated hosts - a
/// health directorate, a municipality, a university, a gas company - failed in
/// the same second. None at all between midnight and six, when nobody is using
/// the machine.
///
/// Two costs came out of not drawing it. robots.txt held the verdict for fifteen
/// minutes, so 112 polls were skipped while other hosts were answering normally.
/// And a failed fetch counts towards the ten that deactivate a source: the
/// longest outage measured was 98 minutes, about six consecutive failures for
/// every source at once, so a longer one would have switched off all ninety.
/// </remarks>
public sealed class NetworkOutageTests
{
    private static HttpFetcherOptions Immediate => new()
    {
        ContactEmail = "team@example.org",
        MinimumHostInterval = TimeSpan.Zero,
        MaximumJitter = TimeSpan.Zero,
        RetryBaseDelay = TimeSpan.FromMilliseconds(1)
    };

    private static HttpRequestException NameDidNotResolve()
        => new(HttpRequestError.NameResolutionError, "No such host is known.");

    /// <summary>A stub whose network can be switched off, the way this one is.</summary>
    private sealed class Switchable : HttpMessageHandler
    {
        public bool NetworkIsUp { get; set; } = true;

        public List<Uri> Reached { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (!NetworkIsUp)
            {
                // Resolution fails for everything at once, which is what the log
                // shows: robots.txt and the page alike.
                throw NameDidNotResolve();
            }

            var uri = request.RequestUri!;
            Reached.Add(uri);

            return Task.FromResult(uri.AbsolutePath == "/robots.txt"
                ? StubHttpMessageHandler.NotFound()
                : StubHttpMessageHandler.Text(HttpStatusCode.OK, "<html>ihale listesi</html>"));
        }
    }

    [Fact]
    public async Task One_host_failing_alone_is_still_that_hosts_failure()
    {
        // Nothing to compare it with means it is theirs. A domain really did
        // stop resolving here once, and it has to keep reaching quarantine.
        var handler = new Switchable();
        using var httpClient = new HttpClient(handler);
        using var fetcher = new HttpFetcher(
            httpClient, Immediate, TimeProvider.System, NullLogger<HttpFetcher>.Instance);

        // robots.txt answers first and is cached, so the page is what fails -
        // the order every poll takes once a source has been seen before.
        await fetcher.FetchAsync(new FetchRequest("https://gone.bel.tr/ihaleler"), CancellationToken.None);
        handler.NetworkIsUp = false;

        var result = await fetcher.FetchAsync(
            new FetchRequest("https://gone.bel.tr/ihaleler"),
            CancellationToken.None);

        Assert.Equal(FetchOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task A_second_host_failing_at_the_same_time_makes_it_ours()
    {
        var handler = new Switchable();
        using var httpClient = new HttpClient(handler);
        using var fetcher = new HttpFetcher(
            httpClient, Immediate, TimeProvider.System, NullLogger<HttpFetcher>.Instance);

        // Both robots.txt files read while the network is up, so they are cached
        // and the outage lands on the pages. That is the shape of the real
        // thing: most of the measured failures were pages, not robots.
        await fetcher.FetchAsync(new FetchRequest("https://a.bel.tr/ihaleler"), CancellationToken.None);
        await fetcher.FetchAsync(new FetchRequest("https://b.edu.tr/duyurular"), CancellationToken.None);

        handler.NetworkIsUp = false;

        var first = await fetcher.FetchAsync(
            new FetchRequest("https://a.bel.tr/ihaleler"), CancellationToken.None);
        var second = await fetcher.FetchAsync(
            new FetchRequest("https://b.edu.tr/duyurular"), CancellationToken.None);

        // The first is judged as the host's, because at that moment there is
        // nothing to compare it with. One spurious failure per outage, rather
        // than one per source per poll.
        Assert.Equal(FetchOutcome.Failed, first.Outcome);
        Assert.Equal(FetchOutcome.Unreachable, second.Outcome);
    }

    [Fact]
    public async Task An_outage_is_not_held_against_the_host_for_fifteen_minutes()
    {
        // The measured cost of the old behaviour: a blip lasting seconds cost a
        // source its next poll, 112 times, while other hosts answered normally.
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 25, 13, 1, 0, TimeSpan.Zero));
        var handler = new Switchable { NetworkIsUp = false };
        using var httpClient = new HttpClient(handler);
        using var fetcher = new HttpFetcher(
            httpClient, Immediate, time, NullLogger<HttpFetcher>.Instance);

        // Two hosts down together: this machine, not them.
        await fetcher.FetchAsync(new FetchRequest("https://a.bel.tr/ihaleler"), CancellationToken.None);
        var duringOutage = await fetcher.FetchAsync(
            new FetchRequest("https://b.edu.tr/duyurular"), CancellationToken.None);

        Assert.Equal(FetchOutcome.SkippedByRobots, duringOutage.Outcome);

        handler.NetworkIsUp = true;
        handler.Reached.Clear();
        time.Advance(TimeSpan.FromSeconds(31));

        var afterOutage = await fetcher.FetchAsync(
            new FetchRequest("https://b.edu.tr/duyurular"), CancellationToken.None);

        // Under the old rule this stayed SkippedByRobots for another fourteen
        // minutes, which with a fifteen-minute poll interval meant the source
        // missed its turn entirely.
        Assert.Equal(FetchOutcome.Fetched, afterOutage.Outcome);
        Assert.Contains(handler.Reached, u => u.AbsolutePath == "/duyurular");
    }

    [Fact]
    public void A_refused_connection_is_not_an_absent_network()
    {
        // The name resolved, so dns worked and something real said no. That is
        // an answer about the host, and it has to keep counting as one.
        Assert.False(NameResolutionWatch.IsNameResolutionFailure(
            new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused")));

        Assert.True(NameResolutionWatch.IsNameResolutionFailure(NameDidNotResolve()));
    }

    [Fact]
    public void A_success_anywhere_clears_the_suspicion()
    {
        var watch = new NameResolutionWatch();

        Assert.False(watch.LooksLikeOurNetwork("a.bel.tr"));
        Assert.True(watch.LooksLikeOurNetwork("b.edu.tr"));

        // One host answering proves the machine is on the network, so the next
        // failure starts the count again rather than inheriting a verdict.
        watch.Reached();

        Assert.False(watch.LooksLikeOurNetwork("c.gov.tr"));
    }

    [Fact]
    public void Asking_twice_about_one_host_is_not_two_hosts()
    {
        // The fetcher and the robots guard both ask, about the same host, in the
        // same poll. If that counted twice, every failure would look like an
        // outage and no source could ever be deactivated again.
        var watch = new NameResolutionWatch();

        Assert.False(watch.LooksLikeOurNetwork("a.bel.tr"));
        Assert.False(watch.LooksLikeOurNetwork("a.bel.tr"));
        Assert.False(watch.LooksLikeOurNetwork("A.BEL.TR"));
    }
}
