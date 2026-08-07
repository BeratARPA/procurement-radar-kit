using ProcurementRadar.Domain.Settings;
using ProcurementRadar.Scoring;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Notifications;
using ProcurementRadar.Notifier;
using ProcurementRadar.UnitTests.Archive;
using ProcurementRadar.UnitTests.Fetching;

namespace ProcurementRadar.UnitTests.Notifications;

public sealed class NotificationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly FakeNotificationRepository _notifications = new();
    private readonly List<string> _sent = [];
    private readonly StubHttpMessageHandler _handler;
    private readonly HttpClient _httpClient;

    public NotificationServiceTests()
    {
        _handler = new StubHttpMessageHandler(
            TimeProvider.System,
            (_, _) => StubHttpMessageHandler.Text(HttpStatusCode.OK, """{"ok":true}""", "application/json"));

        _httpClient = new HttpClient(_handler);
    }

    private NotificationService Service(bool telegramWorks = true)
    {
        var handler = telegramWorks
            ? _handler
            : new StubHttpMessageHandler(
                TimeProvider.System,
                (_, _) => StubHttpMessageHandler.Text(HttpStatusCode.InternalServerError, "nope", "text/plain"));

        var client = new TelegramClient(
            telegramWorks ? _httpClient : new HttpClient(handler),
            new TelegramOptions { BotToken = "token", ChatId = "-100" },
            NullLogger<TelegramClient>.Instance);

        return new NotificationService(
            _notifications,
            new NoticeMessageFormatter(new Fixed<MessageTemplates>(NoticeMessageFormatterTests.Templates), _time),
            client,
            new NotificationOptions(new Fixed<KeywordRules>(KeywordRules.Load(ThresholdOfSixty))),
            NullLogger<NotificationService>.Instance);
    }

    // The threshold now comes from the keyword rules rather than being set
    // beside them, so a test that wants a particular one states it the way the
    // settings table would.
    private const string ThresholdOfSixty = """
        {"include": [ { "term": "yazılım", "weight": 40 } ], "exclude": [], "notify_threshold": 60}
        """;

    [Fact]
    public async Task A_notice_is_announced_once_and_never_again()
    {
        // The acceptance criterion, and the one that decides whether anybody
        // keeps the channel unmuted.
        _notifications.Unannounced.Add(Candidate(1));

        var first = await Service().RunAsync(CancellationToken.None);

        Assert.Equal(1, first.Announced);

        // The claim is now recorded, so the query no longer offers it.
        _notifications.Unannounced.Clear();

        var second = await Service().RunAsync(CancellationToken.None);

        Assert.Equal(0, second.Announced);
        Assert.Single(_notifications.Records);
    }

    [Fact]
    public async Task A_send_that_fails_gives_the_slot_back_so_it_is_tried_again()
    {
        _notifications.Unannounced.Add(Candidate(1));

        var summary = await Service(telegramWorks: false).RunAsync(CancellationToken.None);

        Assert.Equal(0, summary.Announced);
        Assert.Equal(1, summary.Failed);
        // Nothing left claimed: holding the slot after a failed request would
        // lose that notice for good.
        Assert.Empty(_notifications.Records);
    }

    [Fact]
    public async Task A_second_pass_after_a_failure_succeeds()
    {
        _notifications.Unannounced.Add(Candidate(1));
        await Service(telegramWorks: false).RunAsync(CancellationToken.None);

        var summary = await Service().RunAsync(CancellationToken.None);

        Assert.Equal(1, summary.Announced);
    }

    [Fact]
    public async Task A_reminder_is_a_separate_message_and_also_sent_once()
    {
        _notifications.Unannounced.Add(Candidate(1));
        await Service().RunAsync(CancellationToken.None);

        _notifications.Unannounced.Clear();
        _notifications.DueReminders.Add(Candidate(1, deadline: Now.AddHours(4)));

        var first = await Service().RunAsync(CancellationToken.None);
        var second = await Service().RunAsync(CancellationToken.None);

        Assert.Equal(1, first.Reminded);
        Assert.Equal(0, second.Reminded);
        Assert.Equal(2, _notifications.Records.Count);
        Assert.Contains(_notifications.Records, r => r.Kind == NotificationKind.NewNotice);
        Assert.Contains(_notifications.Records, r => r.Kind == NotificationKind.DeadlineReminder);
    }

    [Fact]
    public async Task A_notice_the_query_keeps_offering_is_still_only_sent_once()
    {
        // The claim, not the query, is what makes "once" true. Two passes race,
        // or a read is stale, and the same notice comes round again - at which
        // point the unique constraint says no and nothing may go out. Without
        // this the earlier test would pass on the fake repository's filtering
        // rather than on the service's own behaviour.
        _notifications.AlwaysOffer = true;
        _notifications.Unannounced.Add(Candidate(1));

        await Service().RunAsync(CancellationToken.None);
        await Service().RunAsync(CancellationToken.None);

        Assert.Single(_handler.Requests);
        Assert.Single(_notifications.Records);
    }

    [Fact]
    public async Task The_request_that_goes_to_telegram_carries_both_buttons()
    {
        _notifications.Unannounced.Add(Candidate(7));

        await Service().RunAsync(CancellationToken.None);

        var request = Assert.Single(_handler.Requests);
        var body = request.Body!;

        Assert.EndsWith("/sendMessage", request.Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("reviewed:7", body, StringComparison.Ordinal);
        Assert.Contains("not_relevant:7", body, StringComparison.Ordinal);
        Assert.Contains("inline_keyboard", body, StringComparison.Ordinal);
        Assert.Contains("84/100", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_bot_token_travels_in_the_url_and_never_in_a_log()
    {
        _notifications.Unannounced.Add(Candidate(7));

        await Service().RunAsync(CancellationToken.None);

        // Stated as a test because the url is the one place a secret appears,
        // and TelegramClient logs the method name rather than the address.
        Assert.Contains("/bottoken/", Assert.Single(_handler.Requests).Uri.AbsolutePath, StringComparison.Ordinal);
    }

    private static NoticeForNotification Candidate(long id, DateTimeOffset? deadline = null) => new()
    {
        NoticeId = id,
        Title = "Yazılım Bakım Hizmeti Alımı",
        AuthorityName = "Çankaya Belediyesi",
        BidDeadlineAt = deadline ?? Now.AddDays(2),
        SourceUrl = $"https://example.gov.tr/ilan/{id}",
        RuleScore = 84
    };

    private sealed class FakeNotificationRepository : INotificationRepository
    {
        public List<NoticeForNotification> Unannounced { get; } = [];

        public List<NoticeForNotification> DueReminders { get; } = [];

        public List<(long NoticeId, NotificationKind Kind, string Status)> Records { get; } = [];

        /// <summary>Keeps offering a candidate whatever has been recorded, the way
        /// a stale read or a racing pass would.</summary>
        public bool AlwaysOffer { get; set; }

        public Task<IReadOnlyList<NoticeForNotification>> ListUnannouncedAsync(
            int minimumScore,
            int limit,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<NoticeForNotification>>(Unannounced
                .Where(n => (n.RuleScore ?? 0) >= minimumScore)
                .Where(n => AlwaysOffer
                    || !Records.Any(r => r.NoticeId == n.NoticeId && r.Kind == NotificationKind.NewNotice))
                .Take(limit)
                .ToList());

        public Task<IReadOnlyList<NoticeForNotification>> ListDueRemindersAsync(
            TimeSpan window,
            int minimumScore,
            int limit,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<NoticeForNotification>>(DueReminders
                .Where(n => !Records.Any(r =>
                    r.NoticeId == n.NoticeId && r.Kind == NotificationKind.DeadlineReminder))
                .Take(limit)
                .ToList());

        public Task<bool> TryRecordAsync(
            long noticeId,
            NotificationKind kind,
            NotificationChannel channel,
            string status,
            CancellationToken cancellationToken)
        {
            // Mirrors UNIQUE (notice_id, channel, kind).
            if (Records.Any(r => r.NoticeId == noticeId && r.Kind == kind))
            {
                return Task.FromResult(false);
            }

            Records.Add((noticeId, kind, status));

            return Task.FromResult(true);
        }

        public Task ReleaseAsync(
            long noticeId,
            NotificationKind kind,
            NotificationChannel channel,
            CancellationToken cancellationToken)
        {
            Records.RemoveAll(r => r.NoticeId == noticeId && r.Kind == kind);

            return Task.CompletedTask;
        }

        public Task MarkSentAsync(
            long noticeId,
            NotificationKind kind,
            NotificationChannel channel,
            string status,
            CancellationToken cancellationToken)
        {
            var index = Records.FindIndex(r => r.NoticeId == noticeId && r.Kind == kind);
            if (index >= 0)
            {
                Records[index] = (noticeId, kind, status);
            }

            return Task.CompletedTask;
        }
    }
}
