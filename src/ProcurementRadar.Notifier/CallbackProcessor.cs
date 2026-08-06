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
    private readonly CommandResponder _commands;
    private readonly ILogger<CallbackProcessor> _logger;

    public CallbackProcessor(
        TelegramClient telegram,
        INoticeRepository notices,
        MessageTemplates messages,
        CommandResponder commands,
        ILogger<CallbackProcessor> logger)
    {
        _commands = commands;
        _telegram = telegram;
        _notices = notices;
        _messages = messages;
        _logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var updates = await _telegram.GetUpdatesAsync(cancellationToken).ConfigureAwait(false);
        var applied = 0;

        foreach (var command in updates.Commands)
        {
            try
            {
                await _commands.RespondAsync(command.Name, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Answering a command is the least important thing this loop
                // does. A failure here must not take the button presses with it -
                // those change what the team has decided, and losing them loses
                // the data the filter is calibrated from.
                _logger.LogError(exception, "Could not answer {Command}", command.Name);
            }
        }

        foreach (var callback in updates.Callbacks)
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

            // And the message keeps the answer. The toast is gone in three
            // seconds; this is where the decision lives, because Telegram is the
            // only interface this team has.
            await _telegram
                .EditButtonsAsync(callback.MessageId, Buttons(status, noticeId), cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Notice {NoticeId} moved to {Status} from Telegram", noticeId, status);
            applied++;
        }

        return applied;
    }

    /// <summary>
    /// The keyboard with the chosen option ticked. Both stay pressable: a notice
    /// dismissed on its title often reads differently once the şartname is open.
    /// </summary>
    private IReadOnlyList<TelegramButton> Buttons(NoticeStatus chosen, long noticeId) =>
    [
        new TelegramButton(
            Mark(_messages.Buttons.Review, chosen == NoticeStatus.Reviewed),
            CallbackData.For(NoticeStatus.Reviewed, noticeId)),
        new TelegramButton(
            Mark(_messages.Buttons.NotRelevant, chosen == NoticeStatus.NotRelevant),
            CallbackData.For(NoticeStatus.NotRelevant, noticeId))
    ];

    private static string Mark(string label, bool chosen) => chosen ? "✅ " + label : label;
}
