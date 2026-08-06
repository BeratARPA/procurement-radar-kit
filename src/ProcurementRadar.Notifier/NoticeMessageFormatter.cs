using System.Globalization;
using System.Text;
using ProcurementRadar.Domain.Notifications;

namespace ProcurementRadar.Notifier;

public sealed record ComposedMessage(string Text, string ReviewLabel, string NotRelevantLabel);

/// <summary>
/// Turns a notice into the message in spec Bölüm 10.
/// </summary>
/// <remarks>
/// Every word comes from config/tr-messages.json; the only thing decided here is
/// which line appears and how a date and a duration are written. Dates are shown
/// in Istanbul time and in Turkish, because that is what the reader is comparing
/// against the authority's own page.
/// </remarks>
public sealed class NoticeMessageFormatter
{
    /// <summary>Enough to judge a tender, short enough to read on a phone.</summary>
    private const int ExcerptLength = 320;

    private static readonly System.Text.RegularExpressions.Regex CollapseWhitespace =
        new(@"\s+", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private readonly MessageTemplates _templates;
    private readonly TimeProvider _timeProvider;

    public NoticeMessageFormatter(MessageTemplates templates, TimeProvider timeProvider)
    {
        _templates = templates;
        _timeProvider = timeProvider;
    }

    public ComposedMessage Compose(NoticeForNotification notice, NotificationKind kind)
    {
        var lines = new List<string>();

        if (kind == NotificationKind.DeadlineReminder)
        {
            lines.Add(_templates.ReminderHeader);
        }

        var score = notice.RuleScore ?? 0;

        lines.Add(Fill(_templates.Notice.Header, new()
        {
            ["icon"] = _templates.IconFor(score),
            ["score"] = score.ToString(CultureInfo.InvariantCulture),
            ["title"] = notice.Title
        }));

        lines.Add(Fill(_templates.Notice.Authority, new() { ["authority"] = notice.AuthorityName }));

        if (notice.EstimatedCost is { } cost)
        {
            lines.Add(Fill(_templates.Notice.Cost, new()
            {
                ["cost"] = cost.ToString("N0", Turkish) + " TL"
            }));
        }

        // The most important line in the message, and the one most likely to be
        // missing. When it is, say so rather than leaving a gap somebody reads
        // as "no hurry".
        lines.Add(notice.BidDeadlineAt is { } deadline
            ? Fill(_templates.Notice.Deadline, new()
            {
                ["deadline"] = TimeZoneInfo.ConvertTime(deadline, Istanbul)
                    .ToString("d MMMM HH:mm", Turkish),
                ["remaining"] = Remaining(deadline)
            })
            : _templates.Notice.DeadlineUnknown);

        if (notice.DocumentCount > 0)
        {
            lines.Add(Fill(_templates.Notice.Documents, new()
            {
                ["count"] = notice.DocumentCount.ToString(CultureInfo.InvariantCulture)
            }));
        }

        if (!string.IsNullOrWhiteSpace(notice.Ikn))
        {
            lines.Add(Fill(_templates.Notice.Ikn, new() { ["ikn"] = notice.Ikn }));
        }

        if (Excerpt(notice.DocumentExcerpt) is { } excerpt)
        {
            lines.Add(Fill(_templates.Notice.Excerpt, new() { ["excerpt"] = excerpt }));
        }

        if (!string.IsNullOrWhiteSpace(notice.Rationale))
        {
            lines.Add(Fill(_templates.Notice.Rationale, new() { ["rationale"] = notice.Rationale }));
        }

        if (!string.IsNullOrWhiteSpace(notice.Description))
        {
            lines.Add(string.Empty);
            lines.Add(Shorten(notice.Description, 400));
        }

        lines.Add(string.Empty);
        lines.Add(notice.SourceUrl);

        return new ComposedMessage(
            string.Join('\n', lines),
            _templates.Buttons.Review,
            _templates.Buttons.NotRelevant);
    }

    private string Remaining(DateTimeOffset deadline)
    {
        var left = deadline - _timeProvider.GetUtcNow();

        if (left <= TimeSpan.Zero)
        {
            return _templates.Remaining.Expired;
        }

        // One unit only. "kalan 2 gün 7 saat 14 dakika" is not a number anybody
        // acts on differently from "kalan 2 gün".
        if (left.TotalDays >= 1)
        {
            return Fill(_templates.Remaining.Days, Count(left.TotalDays));
        }

        return left.TotalHours >= 1
            ? Fill(_templates.Remaining.Hours, Count(left.TotalHours))
            : Fill(_templates.Remaining.Minutes, Count(left.TotalMinutes));
    }

    private static Dictionary<string, string> Count(double value) => new()
    {
        ["count"] = ((int)value).ToString(CultureInfo.InvariantCulture)
    };

    private static string Shorten(string text, int limit)
    {
        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return collapsed.Length <= limit ? collapsed : collapsed[..limit].TrimEnd() + "…";
    }

    /// <summary>
    /// Replaces {name} with a value. A placeholder nobody supplies is left
    /// alone, so a typo in the config shows up in the message instead of
    /// throwing at three in the morning.
    /// </summary>
    /// <summary>
    /// The opening of the specification, cut to something readable on a phone.
    /// </summary>
    /// <remarks>
    /// This is the line that answers the question a title cannot: "yazılım
    /// geliştirme" tells you nothing about whether it is an HR system or a
    /// point-of-sale one. Taken from the start because Turkish specifications
    /// lead with "İhale konusu işin niteliği, türü ve miktarı", which is exactly
    /// that answer.
    ///
    /// Extraction leaves pdf layout behind as runs of whitespace and stray line
    /// breaks, so it is collapsed first; without that a message arrives shaped
    /// like a column of confetti.
    /// </remarks>
    private static string? Excerpt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var collapsed = CollapseWhitespace.Replace(text, " ").Trim();

        if (collapsed.Length <= ExcerptLength)
        {
            return collapsed;
        }

        // Cut on a word rather than mid-syllable, then say it was cut.
        var cut = collapsed[..ExcerptLength];
        var lastSpace = cut.LastIndexOf(' ');

        return (lastSpace > ExcerptLength / 2 ? cut[..lastSpace] : cut) + "…";
    }

    private static string Fill(string template, Dictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(template);

        foreach (var (name, value) in values)
        {
            builder.Replace("{" + name + "}", value);
        }

        return builder.ToString();
    }
}
