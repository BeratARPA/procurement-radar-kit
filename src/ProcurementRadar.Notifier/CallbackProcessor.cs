using System.Globalization;
using Microsoft.Extensions.Logging;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Repositories;

namespace ProcurementRadar.Notifier;

/// <summary>
/// The payload behind a button. Kept in one place so the sender and the reader
/// cannot drift apart.
/// </summary>
public static class CallbackData
{
    public static string For(NoticeStatus status, long noticeId)
        => $"{EnumText.ToText(status)}:{noticeId.ToString(CultureInfo.InvariantCulture)}";

    public static bool TryParse(string? data, out NoticeStatus status, out long noticeId)
    {
        status = default;
        noticeId = 0;

        if (string.IsNullOrWhiteSpace(data))
        {
            return false;
        }

        var separator = data.LastIndexOf(':');

        return separator > 0
            && EnumText.TryParse(data[..separator], out status)
            && long.TryParse(data[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out noticeId);
    }
}

/// <summary>
/// Reads the button presses and moves the notice's status.
/// </summary>
/// <remarks>
/// This is not a convenience. Those two buttons are the only record of whether
/// the filter was right, and spec Bölüm 10 keeps them for exactly that: six
/// months of them is what says whether the keyword list is working.
/// </remarks>
public sealed class CallbackProcessor
{
    private readonly TelegramClient _telegram;
    private readonly INoticeRepository _notices;
    private readonly MessageTemplates _messages;
    private readonly ILogger<CallbackProcessor> _logger;

    public CallbackProcessor(
        TelegramClient telegram,
        INoticeRepository notices,
        MessageTemplates messages,
        ILogger<CallbackProcessor> logger)
    {
        _telegram = telegram;
        _notices = notices;
        _messages = messages;
        _logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var callbacks = await _telegram.GetCallbacksAsync(cancellationToken).ConfigureAwait(false);
        var applied = 0;

        foreach (var callback in callbacks)
        {
            if (!CallbackData.TryParse(callback.Data, out var status, out var noticeId))
            {
                _logger.LogWarning("Ignoring a button press with unreadable data: {Data}", callback.Data);
                await _telegram.AnswerCallbackAsync(callback.Id, null, cancellationToken).ConfigureAwait(false);

                continue;
            }

            await _notices.UpdateStatusAsync(noticeId, status, cancellationToken).ConfigureAwait(false);

            // Answered with text, not silence. The press was already recorded
            // either way, but a button that changes nothing on screen is a button
            // people press twice and then stop trusting.
            await _telegram
                .AnswerCallbackAsync(callback.Id, _messages.Buttons.ConfirmationFor(status), cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Notice {NoticeId} moved to {Status} from Telegram", noticeId, status);
            applied++;
        }

        return applied;
    }
}
