using System.Globalization;
using Microsoft.Extensions.Logging;
using ProcurementRadar.Domain.Notifications;

namespace ProcurementRadar.Notifier;

/// <summary>
/// Answers the slash commands typed in the group.
/// </summary>
/// <remarks>
/// Telegram is the only interface this team has, and until now it could only
/// push. A notification tells you about one notice at the moment it arrives; it
/// cannot answer "what is still open", "did anything come in today" or "is the
/// thing even running". Those are the questions somebody asks on a phone, away
/// from the machine, and they are the reason this exists rather than waiting for
/// the web interface.
///
/// Every reply is built from templates in config/tr-messages.json, like every
/// other message this project sends.
/// </remarks>
public sealed class CommandResponder
{
    private readonly TelegramClient _telegram;
    private readonly ICommandQueries _queries;
    private readonly MessageTemplates _messages;
    private readonly NotificationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CommandResponder> _logger;

    public CommandResponder(
        TelegramClient telegram,
        ICommandQueries queries,
        MessageTemplates messages,
        NotificationOptions options,
        TimeProvider timeProvider,
        ILogger<CommandResponder> logger)
    {
        _telegram = telegram;
        _queries = queries;
        _messages = messages;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RespondAsync(string command, CancellationToken cancellationToken)
    {
        var templates = _messages.Commands;

        // Matched against the config, so the commands themselves are Turkish data
        // rather than identifiers in code.
        var reply = command switch
        {
            _ when Matches(command, templates.OpenCommand) => await OpenAsync(cancellationToken).ConfigureAwait(false),
            _ when Matches(command, templates.TodayCommand) => await TodayAsync(cancellationToken).ConfigureAwait(false),
            _ when Matches(command, templates.StatusCommand) => await StatusAsync(cancellationToken).ConfigureAwait(false),
            _ when Matches(command, templates.HelpCommand) => templates.Help,
            _ => null
        };

        if (reply is null)
        {
            // Unknown commands are ignored rather than answered. A group has other
            // bots and other habits in it, and a radar that argues with every
            // stray slash becomes something people mute.
            return;
        }

        _logger.LogInformation("Answered {Command}", command);

        await _telegram.SendAsync(reply, [], cancellationToken).ConfigureAwait(false);
    }

    private static bool Matches(string command, string configured)
        => string.Equals(command, configured, StringComparison.OrdinalIgnoreCase);

    private async Task<string> OpenAsync(CancellationToken cancellationToken)
    {
        var notices = await _queries
            .ListOpenAsync(_options.MinimumScore, 10, cancellationToken)
            .ConfigureAwait(false);

        return notices.Count == 0
            ? _messages.Commands.NothingOpen
            : _messages.Commands.OpenHeader + "\n\n" + string.Join("\n", notices.Select(Line));
    }

    private async Task<string> TodayAsync(CancellationToken cancellationToken)
    {
        var since = _timeProvider.GetUtcNow().AddHours(-24);
        var notices = await _queries.ListSeenSinceAsync(since, 10, cancellationToken).ConfigureAwait(false);

        return notices.Count == 0
            ? _messages.Commands.NothingToday
            : _messages.Commands.TodayHeader + "\n\n" + string.Join("\n", notices.Select(Line));
    }

    private async Task<string> StatusAsync(CancellationToken cancellationToken)
    {
        var status = await _queries.GetStatusAsync(cancellationToken).ConfigureAwait(false);

        return _messages.Commands.Status
            .Replace("{active}", status.ActiveSources.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{failing}", status.FailingSources.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{notices}", status.Notices.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{today}", status.NoticesToday.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{announced}", status.Announced.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(
                "{lastFetch}",
                status.LastFetchAt is { } at
                    ? at.ToOffset(TimeSpan.FromHours(3)).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)
                    : "-",
                StringComparison.Ordinal);
    }

    /// <summary>
    /// One notice on one line: score, deadline, title. Enough to decide whether
    /// to scroll back and find the notification it came from.
    /// </summary>
    private string Line(CommandNotice notice)
        => _messages.Commands.Line
            .Replace("{icon}", _messages.IconFor(notice.Score), StringComparison.Ordinal)
            .Replace("{score}", notice.Score.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(
                "{deadline}",
                notice.BidDeadlineAt is { } deadline
                    ? deadline.ToOffset(TimeSpan.FromHours(3)).ToString("dd.MM HH:mm", CultureInfo.InvariantCulture)
                    : "-",
                StringComparison.Ordinal)
            .Replace("{title}", Shorten(notice.Title), StringComparison.Ordinal);

    private static string Shorten(string title)
        => title.Length <= 70 ? title : title[..70].TrimEnd() + "…";
}
