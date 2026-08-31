using Microsoft.Extensions.Logging;
using ProcurementRadar.Collector;
using ProcurementRadar.Notifier;

namespace ProcurementRadar.UnitTests.Notifications;

/// <summary>
/// Telegram is not down when this machine is.
/// </summary>
/// <remarks>
/// D-30 drew this line for the collector and stopped there, so the same outage
/// went on being reported here: 24 errors in one of them, 628 across three
/// weeks, every one of them naming api.telegram.org for minutes in which no
/// host on the internet was reachable from this machine. D-07 counts errors in
/// the weekly report, which is the only reason the level matters - what the
/// call returns is unchanged either way.
///
/// The watch has to be the same object the collector uses. An instance that
/// only ever sees api.telegram.org can never reach the two distinct names that
/// separate an outage from a domain that has actually died, so a per-client
/// watch would make this rule permanently inert - it would compile, pass a test
/// written against it alone, and never once fire.
/// </remarks>
public sealed class OutageIsNotTelegramsFaultTests
{
    private static HttpRequestException NameDidNotResolve(string host)
        => new(HttpRequestError.NameResolutionError, $"No such host is known. ({host}:443)");

    private static TelegramClient ClientOver(
        HttpMessageHandler handler,
        NameResolutionWatch watch,
        ILogger<TelegramClient> logger) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org/") },
        new TelegramOptions { BotToken = "test-token", ChatId = "1", LongPollSeconds = 1 },
        logger,
        watch);

    [Fact]
    public async Task A_lone_telegram_failure_is_still_reported()
    {
        // Nothing else is failing, so as far as this machine can tell Telegram
        // is the thing that is wrong. That has to keep reaching the report.
        var logger = new LevelCapture();
        var client = ClientOver(new AlwaysThrows(NameDidNotResolve("api.telegram.org")), new NameResolutionWatch(), logger);

        var sent = await client.SendAsync("ilan", [], CancellationToken.None);

        Assert.False(sent);
        Assert.Contains(LogLevel.Error, logger.Levels);
    }

    [Fact]
    public async Task An_outage_the_collector_already_saw_is_not_blamed_on_telegram()
    {
        // The shape of the real thing: the collector has just failed to look up
        // two authorities, and the callback loop comes round a second later.
        var watch = new NameResolutionWatch();
        watch.LooksLikeOurNetwork("ankaraeah.saglik.gov.tr");
        watch.LooksLikeOurNetwork("www.kecioren.bel.tr");

        var logger = new LevelCapture();
        var client = ClientOver(new AlwaysThrows(NameDidNotResolve("api.telegram.org")), watch, logger);

        var sent = await client.SendAsync("ilan", [], CancellationToken.None);

        // Still false - nothing was sent and the caller must know that. Only the
        // level changed, because nobody needs to look into this one.
        Assert.False(sent);
        Assert.DoesNotContain(LogLevel.Error, logger.Levels);
        Assert.Contains(LogLevel.Debug, logger.Levels);
    }

    [Fact]
    public async Task A_telegram_error_that_is_not_a_name_lookup_is_still_an_error()
    {
        // The connection was made and something real refused it. That is an
        // answer about Telegram however bad this machine's day has been.
        var watch = new NameResolutionWatch();
        watch.LooksLikeOurNetwork("a.bel.tr");
        watch.LooksLikeOurNetwork("b.edu.tr");

        var logger = new LevelCapture();
        var client = ClientOver(
            new AlwaysThrows(new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused")),
            watch,
            logger);

        Assert.False(await client.SendAsync("ilan", [], CancellationToken.None));
        Assert.Contains(LogLevel.Error, logger.Levels);
    }

    [Fact]
    public void One_host_cannot_decide_this_on_its_own()
    {
        // The whole reason the watch is shared, stated as a test: asked only
        // about Telegram, it can never say yes.
        var alone = new NameResolutionWatch();

        Assert.False(alone.IsOurOutage(NameDidNotResolve("api.telegram.org"), "api.telegram.org"));
        Assert.False(alone.IsOurOutage(NameDidNotResolve("api.telegram.org"), "api.telegram.org"));
    }

    private sealed class AlwaysThrows : HttpMessageHandler
    {
        private readonly Exception _exception;

        public AlwaysThrows(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => throw _exception;
    }

    private sealed class LevelCapture : ILogger<TelegramClient>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Levels.Add(logLevel);
    }
}
