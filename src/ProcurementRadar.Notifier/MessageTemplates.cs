using System.Text.Json;
using ProcurementRadar.Domain.Enums;
using System.Text.Json.Serialization;

namespace ProcurementRadar.Notifier;

/// <summary>config/tr-messages.json, loaded.</summary>
/// <remarks>
/// The wording of a notification is the part of this system that gets adjusted
/// most often and by whoever is reading the messages, so it lives in a file
/// rather than in a rebuild.
/// </remarks>
public sealed class MessageTemplates
{
    private MessageTemplates(
        NoticeTemplates notice,
        string reminderHeader,
        ButtonLabels buttons,
        RemainingLabels remaining,
        IReadOnlyList<ScoreIcon> scoreIcons,
        AlertTemplates alerts,
        WeeklySummaryTemplates weeklySummary)
    {
        Alerts = alerts;
        WeeklySummary = weeklySummary;
        Notice = notice;
        ReminderHeader = reminderHeader;
        Buttons = buttons;
        Remaining = remaining;
        ScoreIcons = scoreIcons;
    }

    public NoticeTemplates Notice { get; }

    public string ReminderHeader { get; }

    public ButtonLabels Buttons { get; }

    public RemainingLabels Remaining { get; }

    /// <summary>Highest threshold first, so the first match is the right one.</summary>
    public IReadOnlyList<ScoreIcon> ScoreIcons { get; }

    public AlertTemplates Alerts { get; }

    public WeeklySummaryTemplates WeeklySummary { get; }

    public string IconFor(int score)
        => ScoreIcons.FirstOrDefault(icon => score >= icon.AtLeast)?.Icon ?? string.Empty;

    public static MessageTemplates Load(string json)
    {
        var shape = JsonSerializer.Deserialize<Shape>(json)
            ?? throw new InvalidOperationException("tr-messages.json deserialized to null.");

        if (string.IsNullOrWhiteSpace(shape.Notice.Header))
        {
            throw new InvalidOperationException("tr-messages.json has no notice.header template.");
        }

        return new MessageTemplates(
            shape.Notice,
            shape.ReminderHeader,
            shape.Buttons,
            shape.Remaining,
            shape.ScoreIcons.OrderByDescending(icon => icon.AtLeast).ToArray(),
            shape.Alerts,
            shape.WeeklySummary);
    }

    public sealed record NoticeTemplates
    {
        [JsonPropertyName("header")]
        public string Header { get; init; } = string.Empty;

        [JsonPropertyName("authority")]
        public string Authority { get; init; } = string.Empty;

        [JsonPropertyName("cost")]
        public string Cost { get; init; } = string.Empty;

        [JsonPropertyName("deadline")]
        public string Deadline { get; init; } = string.Empty;

        [JsonPropertyName("deadline_unknown")]
        public string DeadlineUnknown { get; init; } = string.Empty;

        [JsonPropertyName("documents")]
        public string Documents { get; init; } = string.Empty;

        [JsonPropertyName("ikn")]
        public string Ikn { get; init; } = string.Empty;

        [JsonPropertyName("excerpt")]
        public string Excerpt { get; init; } = string.Empty;

        [JsonPropertyName("rationale")]
        public string Rationale { get; init; } = string.Empty;
    }

    public sealed record ButtonLabels
    {
        [JsonPropertyName("review")]
        public string Review { get; init; } = string.Empty;

        [JsonPropertyName("not_relevant")]
        public string NotRelevant { get; init; } = string.Empty;

        /// <summary>
        /// Shown as a toast after the press. Without one Telegram simply stops
        /// the spinner and leaves the message exactly as it was, so a button
        /// that worked perfectly looks like a button that did nothing.
        /// </summary>
        [JsonPropertyName("review_confirmation")]
        public string ReviewConfirmation { get; init; } = string.Empty;

        [JsonPropertyName("not_relevant_confirmation")]
        public string NotRelevantConfirmation { get; init; } = string.Empty;

        public string ConfirmationFor(NoticeStatus status)
            => status == NoticeStatus.NotRelevant ? NotRelevantConfirmation : ReviewConfirmation;
    }

    public sealed record RemainingLabels
    {
        [JsonPropertyName("days")]
        public string Days { get; init; } = string.Empty;

        [JsonPropertyName("hours")]
        public string Hours { get; init; } = string.Empty;

        [JsonPropertyName("minutes")]
        public string Minutes { get; init; } = string.Empty;

        [JsonPropertyName("expired")]
        public string Expired { get; init; } = string.Empty;
    }

    public sealed record AlertTemplates
    {
        [JsonPropertyName("fetch_failures")]
        public string FetchFailures { get; init; } = string.Empty;

        [JsonPropertyName("source_deactivated")]
        public string SourceDeactivated { get; init; } = string.Empty;

        [JsonPropertyName("parser_broken")]
        public string ParserBroken { get; init; } = string.Empty;

        [JsonPropertyName("silent_source")]
        public string SilentSource { get; init; } = string.Empty;

        [JsonPropertyName("nothing_collected")]
        public string NothingCollected { get; init; } = string.Empty;
    }

    public sealed record WeeklySummaryTemplates
    {
        [JsonPropertyName("header")]
        public string Header { get; init; } = string.Empty;

        [JsonPropertyName("lines")]
        public IReadOnlyList<string> Lines { get; init; } = [];
    }

    public sealed record ScoreIcon
    {
        [JsonPropertyName("at_least")]
        public int AtLeast { get; init; }

        [JsonPropertyName("icon")]
        public string Icon { get; init; } = string.Empty;
    }

    private sealed record Shape
    {
        [JsonPropertyName("notice")]
        public NoticeTemplates Notice { get; init; } = new();

        [JsonPropertyName("reminder_header")]
        public string ReminderHeader { get; init; } = string.Empty;

        [JsonPropertyName("buttons")]
        public ButtonLabels Buttons { get; init; } = new();

        [JsonPropertyName("remaining")]
        public RemainingLabels Remaining { get; init; } = new();

        [JsonPropertyName("score_icons")]
        public IReadOnlyList<ScoreIcon> ScoreIcons { get; init; } = [];

        [JsonPropertyName("alerts")]
        public AlertTemplates Alerts { get; init; } = new();

        [JsonPropertyName("weekly_summary")]
        public WeeklySummaryTemplates WeeklySummary { get; init; } = new();
    }
}
