using Microsoft.Extensions.Logging;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Notifications;

namespace ProcurementRadar.Notifier;

public sealed record NotificationRunSummary(int Announced, int Reminded, int Failed);

/// <summary>
/// Decides what is worth a message and sends it exactly once.
/// </summary>
public sealed class NotificationService
{
    private const string Pending = "pending";
    private const string Sent = "sent";

    private readonly INotificationRepository _notifications;
    private readonly NoticeMessageFormatter _formatter;
    private readonly TelegramClient _telegram;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        INotificationRepository notifications,
        NoticeMessageFormatter formatter,
        TelegramClient telegram,
        NotificationOptions options,
        ILogger<NotificationService> logger)
    {
        _notifications = notifications;
        _formatter = formatter;
        _telegram = telegram;
        _options = options;
        _logger = logger;
    }

    public async Task<NotificationRunSummary> RunAsync(CancellationToken cancellationToken)
    {
        var announced = 0;
        var reminded = 0;
        var failed = 0;

        foreach (var notice in await _notifications
            .ListUnannouncedAsync(_options.MinimumScore, _options.BatchSize, cancellationToken)
            .ConfigureAwait(false))
        {
            var sent = await SendAsync(notice, NotificationKind.NewNotice, cancellationToken)
                .ConfigureAwait(false);

            if (sent)
            {
                announced++;
            }
            else
            {
                failed++;
            }
        }

        foreach (var notice in await _notifications
            .ListDueRemindersAsync(
                _options.ReminderWindow,
                _options.MinimumScore,
                _options.BatchSize,
                cancellationToken)
            .ConfigureAwait(false))
        {
            if (await SendAsync(notice, NotificationKind.DeadlineReminder, cancellationToken)
                .ConfigureAwait(false))
            {
                reminded++;
            }
            else
            {
                failed++;
            }
        }

        if (announced + reminded + failed > 0)
        {
            _logger.LogInformation(
                "Notification run: {Announced} announced, {Reminded} reminded, {Failed} failed",
                announced,
                reminded,
                failed);
        }

        return new NotificationRunSummary(announced, reminded, failed);
    }

    private async Task<bool> SendAsync(
        NoticeForNotification notice,
        NotificationKind kind,
        CancellationToken cancellationToken)
    {
        // The slot is claimed before the message goes out. The unique constraint
        // in 005 is what makes "at most one" true rather than a check that could
        // race or be forgotten.
        if (!await _notifications
            .TryRecordAsync(notice.NoticeId, kind, NotificationChannel.Telegram, Pending, cancellationToken)
            .ConfigureAwait(false))
        {
            return false;
        }

        var message = _formatter.Compose(notice, kind);

        var delivered = await _telegram.SendAsync(
            message.Text,
            [
                new TelegramButton(message.ReviewLabel, CallbackData.For(NoticeStatus.Reviewed, notice.NoticeId)),
                new TelegramButton(
                    message.NotRelevantLabel,
                    CallbackData.For(NoticeStatus.NotRelevant, notice.NoticeId))
            ],
            cancellationToken).ConfigureAwait(false);

        if (!delivered)
        {
            // Hand the slot back so the next pass can try again. Keeping it
            // would turn one failed request into a notice nobody ever hears
            // about.
            await _notifications
                .ReleaseAsync(notice.NoticeId, kind, NotificationChannel.Telegram, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogWarning(
                "Could not send the {Kind} message for notice {NoticeId}; will try again",
                kind,
                notice.NoticeId);

            return false;
        }

        await _notifications
            .MarkSentAsync(notice.NoticeId, kind, NotificationChannel.Telegram, Sent, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Sent the {Kind} message for notice {NoticeId} ({Score}/100)",
            kind,
            notice.NoticeId,
            notice.RuleScore);

        return true;
    }
}

public sealed record NotificationOptions
{
    /// <summary>From <c>notify_threshold</c> in keywords.json.</summary>
    public required int MinimumScore { get; init; }

    /// <summary>Spec Bölüm 10: one nudge six hours before the deadline.</summary>
    public TimeSpan ReminderWindow { get; init; } = TimeSpan.FromHours(6);

    public int BatchSize { get; init; } = 20;
}
