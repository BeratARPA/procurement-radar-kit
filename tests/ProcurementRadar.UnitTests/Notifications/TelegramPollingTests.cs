using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using ProcurementRadar.Notifier;

namespace ProcurementRadar.UnitTests.Notifications;

/// <summary>
/// What a poll does when Telegram cannot be asked.
/// </summary>
/// <remarks>
/// The loop that calls this has no delay of its own on purpose: a successful
/// long poll blocks until Telegram has something to say, which paces it. That
/// holds only while the call reaches Telegram. When it cannot, the call returns
/// at once, and if it returns *normally* the caller goes straight round again.
///
/// It did. On 13 August 2026 api.telegram.org stopped resolving for 37 minutes
/// and this wrote 25,925 stack traces - twelve a second. Across four days,
/// 85,711 lines and most of a 318 MB log directory. CallbackWorker has had a
/// thirty second pause after a failure the whole time and could never reach it,
/// because the failure was caught one layer down and returned as "nothing new".
///
/// So these tests assert the unglamorous half of the contract: not what is
/// returned, but that nothing is returned at all.
/// </remarks>
public sealed class TelegramPollingTests
{
    private static TelegramClient ClientOver(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org/") },
        new TelegramOptions
        {
            BotToken = "test-token",
            ChatId = "1",
            LongPollSeconds = 1
        },
        NullLogger<TelegramClient>.Instance);

    [Fact]
    public async Task An_unreachable_telegram_throws_rather_than_reporting_nothing_new()
    {
        // The exact shape of the outage: DNS stops resolving, so the request
        // fails before it leaves the machine.
        var client = ClientOver(new ThrowingHandler(
            new HttpRequestException("No such host is known. (api.telegram.org:443)")));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetUpdatesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_rate_limit_throws_too()
    {
        // 429 is Telegram asking to be left alone. Returning nothing sent the
        // caller back into the same call immediately - the one answer certain
        // to make a rate limit last longer.
        var client = ClientOver(new StatusHandler(HttpStatusCode.TooManyRequests));

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetUpdatesAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.TooManyRequests, thrown.StatusCode);
    }

    [Fact]
    public async Task So_does_a_second_poller_being_told_to_go_away()
    {
        // 409 means something else is already polling with this token. Spinning
        // on it is how one stray process turns into two hot loops.
        var client = ClientOver(new StatusHandler(HttpStatusCode.Conflict));

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetUpdatesAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.Conflict, thrown.StatusCode);
    }

    [Fact]
    public async Task An_empty_poll_is_still_an_empty_poll_and_not_an_error()
    {
        // The other half. Telegram answering "ok, nothing" is the normal case
        // and must stay quiet - a version of this that threw on every idle poll
        // would pause the loop for thirty seconds between every button press.
        var client = ClientOver(new BodyHandler("""{"ok":true,"result":[]}"""));

        var updates = await client.GetUpdatesAsync(CancellationToken.None);

        Assert.Empty(updates.Callbacks);
        Assert.Empty(updates.Commands);
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class BodyHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
    }
}
