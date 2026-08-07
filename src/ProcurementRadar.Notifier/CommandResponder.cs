using System.Globalization;
using Microsoft.Extensions.Logging;
using ProcurementRadar.Domain.Notifications;
using ProcurementRadar.Domain.Repositories;

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
    private readonly ProcurementRadar.Domain.Settings.ICurrent<MessageTemplates> _messages;
    private readonly NotificationOptions _options;
    private readonly Scoring.KeywordRules _keywords;
    private readonly IAwardRepository _awards;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CommandResponder> _logger;

    public CommandResponder(
        TelegramClient telegram,
        ICommandQueries queries,
        ProcurementRadar.Domain.Settings.ICurrent<MessageTemplates> messages,
        NotificationOptions options,
        Scoring.KeywordRules keywords,
        IAwardRepository awards,
        TimeProvider timeProvider,
        ILogger<CommandResponder> logger)
    {
        _telegram = telegram;
        _queries = queries;
        _awards = awards;
        _messages = messages;
        _options = options;
        _keywords = keywords;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RespondAsync(string command, string argument, CancellationToken cancellationToken)
    {
        var templates = _messages.Value.Commands;

        // Matched against the config, so the commands themselves are Turkish data
        // rather than identifiers in code.
        var reply = command switch
        {
            _ when Matches(command, templates.OpenCommand) => await OpenAsync(cancellationToken).ConfigureAwait(false),
            _ when Matches(command, templates.TodayCommand) => await TodayAsync(cancellationToken).ConfigureAwait(false),
            _ when Matches(command, templates.StatusCommand) => await StatusAsync(cancellationToken).ConfigureAwait(false),
            _ when Matches(command, templates.ClosingCommand) => await ClosingAsync(cancellationToken).ConfigureAwait(false),
            _ when Matches(command, templates.SearchCommand) => await SearchAsync(argument, cancellationToken).ConfigureAwait(false),
            _ when Matches(command, templates.DetailCommand) => await DetailAsync(argument, cancellationToken).ConfigureAwait(false),
            _ when Matches(command, templates.SourcesCommand) => await SourcesAsync(cancellationToken).ConfigureAwait(false),
            _ when Matches(command, templates.ReportCommand) => await ReportAsync(cancellationToken).ConfigureAwait(false),
            _ when Matches(command, templates.KeywordsCommand) => Keywords(),
            _ when Matches(command, templates.AwardsCommand) => await AwardsAsync(cancellationToken).ConfigureAwait(false),
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
            ? _messages.Value.Commands.NothingOpen
            : _messages.Value.Commands.OpenHeader + "\n\n" + string.Join("\n", notices.Select(Line));
    }

    private async Task<string> TodayAsync(CancellationToken cancellationToken)
    {
        var since = _timeProvider.GetUtcNow().AddHours(-24);
        var notices = await _queries.ListSeenSinceAsync(since, 10, cancellationToken).ConfigureAwait(false);

        return notices.Count == 0
            ? _messages.Value.Commands.NothingToday
            : _messages.Value.Commands.TodayHeader + "\n\n" + string.Join("\n", notices.Select(Line));
    }

    private async Task<string> StatusAsync(CancellationToken cancellationToken)
    {
        var status = await _queries.GetStatusAsync(cancellationToken).ConfigureAwait(false);

        return _messages.Value.Commands.Status
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
        => _messages.Value.Commands.Line
            .Replace("{icon}", _messages.Value.IconFor(notice.Score), StringComparison.Ordinal)
            .Replace("{score}", notice.Score.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(
                "{deadline}",
                notice.BidDeadlineAt is { } deadline
                    ? deadline.ToOffset(TimeSpan.FromHours(3)).ToString("dd.MM HH:mm", CultureInfo.InvariantCulture)
                    : "-",
                StringComparison.Ordinal)
            .Replace("{title}", Shorten(notice.Title), StringComparison.Ordinal);

    private async Task<string> ClosingAsync(CancellationToken cancellationToken)
    {
        var notices = await _queries.ListClosingSoonAsync(48, 10, cancellationToken).ConfigureAwait(false);

        return notices.Count == 0
            ? _messages.Value.Commands.ClosingEmpty
            : _messages.Value.Commands.ClosingHeader + "\n\n" + string.Join("\n", notices.Select(Line));
    }

    private async Task<string> SearchAsync(string term, CancellationToken cancellationToken)
    {
        if (term.Length < 3)
        {
            // Two letters match half the archive and answer nothing.
            return _messages.Value.Commands.SearchUsage;
        }

        var notices = await _queries.SearchAsync(term, 10, cancellationToken).ConfigureAwait(false);

        return notices.Count == 0
            ? _messages.Value.Commands.SearchEmpty.Replace("{term}", term, StringComparison.Ordinal)
            : _messages.Value.Commands.SearchHeader.Replace("{term}", term, StringComparison.Ordinal)
              + "\n\n" + string.Join("\n", notices.Select(Line));
    }

    private async Task<string> DetailAsync(string ikn, CancellationToken cancellationToken)
    {
        if (ikn.Length < 5)
        {
            return _messages.Value.Commands.DetailUsage;
        }

        var detail = await _queries.GetDetailAsync(ikn, cancellationToken).ConfigureAwait(false);

        if (detail is null)
        {
            return _messages.Value.Commands.DetailMissing.Replace("{ikn}", ikn, StringComparison.Ordinal);
        }

        // Telegram refuses anything over 4096 characters, and an announcement runs
        // to seven thousand. Cut with room left for everything above it.
        var text = detail.AnnouncementText is { Length: > 0 } announcement
            ? (announcement.Length > 3200 ? announcement[..3200] + "…" : announcement)
            : _messages.Value.Commands.DetailNoText;

        return _messages.Value.Commands.Detail
            .Replace("{icon}", _messages.Value.IconFor(detail.Score), StringComparison.Ordinal)
            .Replace("{score}", detail.Score.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{title}", detail.Title, StringComparison.Ordinal)
            .Replace("{authority}", detail.AuthorityName, StringComparison.Ordinal)
            .Replace("{ikn}", detail.Ikn ?? "-", StringComparison.Ordinal)
            .Replace("{deadline}", Moment(detail.BidDeadlineAt), StringComparison.Ordinal)
            .Replace("{rationale}", detail.Rationale ?? "-", StringComparison.Ordinal)
            .Replace("{text}", text, StringComparison.Ordinal);
    }

    private async Task<string> SourcesAsync(CancellationToken cancellationToken)
    {
        var sources = await _queries.ListTroubledSourcesAsync(30, cancellationToken).ConfigureAwait(false);

        if (sources.Count == 0)
        {
            return _messages.Value.Commands.SourcesHealthy;
        }

        var lines = sources.Select(source => _messages.Value.Commands.SourcesLine
            .Replace("{name}", source.AuthorityName, StringComparison.Ordinal)
            .Replace(
                "{failures}",
                source.ConsecutiveFailures.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal)
            .Replace("{lastNotice}", Moment(source.LastNewNoticeAt), StringComparison.Ordinal));

        return _messages.Value.Commands.SourcesHeader + "\n\n" + string.Join("\n", lines);
    }

    private async Task<string> ReportAsync(CancellationToken cancellationToken)
    {
        var rows = await _queries.GetScoreReportAsync(cancellationToken).ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return _messages.Value.Commands.ReportEmpty;
        }

        var lines = rows.Select(row => _messages.Value.Commands.ReportLine
            .Replace("{status}", row.Status, StringComparison.Ordinal)
            .Replace("{count}", row.Count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(
                "{average}",
                row.AverageScore.ToString("0.#", CultureInfo.InvariantCulture),
                StringComparison.Ordinal)
            .Replace("{max}", row.MaximumScore.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));

        return _messages.Value.Commands.ReportHeader + "\n\n" + string.Join("\n", lines)
            + "\n" + _messages.Value.Commands.ReportNote;
    }

    /// <summary>
    /// The filter as it currently stands. Read only: a group has no way to tell
    /// who is typing, and one mistyped term can silence the whole thing.
    /// </summary>
    private string Keywords()
    {
        var header = _messages.Value.Commands.KeywordsHeader.Replace(
            "{threshold}",
            _options.MinimumScore.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

        var include = _keywords.Include
            .OrderByDescending(term => term.Weight)
            .Select(term => $"• {term.Term} ({term.Weight})");

        return header + "\n" + string.Join("\n", include)
            + "\n" + _messages.Value.Commands.KeywordsExcluded + "\n"
            + string.Join(", ", _keywords.Exclude);
    }

    /// <summary>
    /// What the tenders we watched ended up as.
    /// </summary>
    /// <remarks>
    /// The estimate is printed beside the contract amount on purpose. One line
    /// saying a 3.405.000 estimate was signed at 3.300.000 against a single bid
    /// tells you more about how to price the next one than the notification for
    /// that tender ever did.
    /// </remarks>
    private async Task<string> AwardsAsync(CancellationToken cancellationToken)
    {
        var awards = await _awards.ListRecentAsync(10, cancellationToken).ConfigureAwait(false);

        if (awards.Count == 0)
        {
            return _messages.Value.Commands.AwardsEmpty;
        }

        var lines = awards.Select(award => _messages.Value.Commands.AwardsLine
            .Replace("{title}", Shorten(award.Title), StringComparison.Ordinal)
            .Replace("{winner}", award.WinnerName ?? _messages.Value.Commands.AwardsUnknown, StringComparison.Ordinal)
            .Replace("{amount}", Money(award.ContractAmount), StringComparison.Ordinal)
            .Replace(
                "{estimate}",
                award.EstimatedCost is { } estimate
                    ? _messages.Value.Commands.AwardsEstimate.Replace(
                        "{estimate}",
                        Money(estimate),
                        StringComparison.Ordinal)
                    : string.Empty,
                StringComparison.Ordinal)
            .Replace(
                "{bids}",
                award.BidCount?.ToString(CultureInfo.InvariantCulture) ?? _messages.Value.Commands.AwardsUnknown,
                StringComparison.Ordinal)
            .Replace(
                "{date}",
                award.AwardedAt is { } at
                    ? at.ToOffset(TimeSpan.FromHours(3)).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
                    : _messages.Value.Commands.AwardsUnknown,
                StringComparison.Ordinal));

        return _messages.Value.Commands.AwardsHeader + "\n\n" + string.Join("\n", lines);
    }

    private string Money(decimal? amount)
        => amount is { } value
            ? value.ToString("N0", CultureInfo.GetCultureInfo("tr-TR")) + " TL"
            : _messages.Value.Commands.AwardsUnknown;

    private static string Moment(DateTimeOffset? at)
        => at is { } value
            ? value.ToOffset(TimeSpan.FromHours(3)).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)
            : "-";

    private static string Shorten(string title)
        => title.Length <= 70 ? title : title[..70].TrimEnd() + "…";
}
