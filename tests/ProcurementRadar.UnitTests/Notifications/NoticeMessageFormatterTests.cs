using ProcurementRadar.Domain.Settings;
using Microsoft.Extensions.Time.Testing;
using ProcurementRadar.Domain.Notifications;
using ProcurementRadar.Notifier;

namespace ProcurementRadar.UnitTests.Notifications;

public sealed class NoticeMessageFormatterTests
{
    internal static readonly MessageTemplates Templates = MessageTemplates.Load(
        File.ReadAllText(Path.Combine(RepositoryRoot.Find().FullName, "config", "tr-messages.json")));

    // 12:00 on 12 March 2026 in Istanbul.
    private static readonly DateTimeOffset Now = new(2026, 3, 12, 9, 0, 0, TimeSpan.Zero);

    private static readonly FakeTimeProvider Time = new(Now);

    private static readonly NoticeMessageFormatter Formatter = new(new Fixed<MessageTemplates>(Templates), Time);

    private static NoticeForNotification Notice(
        DateTimeOffset? deadline = null,
        decimal? cost = 180000,
        int documents = 0,
        int score = 84) => new()
    {
        NoticeId = 1,
        Title = "Yazılım Bakım ve Destek Hizmeti Alımı",
        AuthorityName = "Çankaya Belediyesi",
        Description = "Mevcut personel takip sistemine modül eklenmesi.",
        EstimatedCost = cost,
        BidDeadlineAt = deadline ?? new DateTimeOffset(2026, 3, 14, 11, 0, 0, TimeSpan.Zero),
        SourceUrl = "https://example.gov.tr/ilan/1",
        RuleScore = score,
        Rationale = "yazılım bakım",
        DocumentCount = documents
    };

    [Fact]
    public void The_message_carries_what_spec_bölüm_10_asks_for()
    {
        var message = Formatter.Compose(Notice(documents: 2), NotificationKind.NewNotice);

        Assert.Contains("84/100", message.Text, StringComparison.Ordinal);
        Assert.Contains("Yazılım Bakım ve Destek Hizmeti Alımı", message.Text, StringComparison.Ordinal);
        Assert.Contains("Çankaya Belediyesi", message.Text, StringComparison.Ordinal);
        Assert.Contains("180.000 TL", message.Text, StringComparison.Ordinal);
        Assert.Contains("2 dosya", message.Text, StringComparison.Ordinal);
        Assert.Contains("modül eklenmesi", message.Text, StringComparison.Ordinal);
        Assert.Contains("https://example.gov.tr/ilan/1", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_deadline_is_written_in_Istanbul_time_and_in_Turkish()
    {
        // Stored as 11:00 UTC, which is 14:00 in Istanbul - and 14:00 is what the
        // authority's own page says, so that is what the reader has to see.
        var message = Formatter.Compose(Notice(), NotificationKind.NewNotice);

        Assert.Contains("14 Mart 14:00", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_deadline_is_called_out_rather_than_left_blank()
    {
        // The acceptance criterion, and the reason for it: a gap where the
        // deadline should be reads as "no hurry".
        var message = Formatter.Compose(
            Notice() with { BidDeadlineAt = null },
            NotificationKind.NewNotice);

        Assert.Contains("okunamadı", message.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Son teklif:", message.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(50, "kalan 2 gün")]
    [InlineData(7, "kalan 7 saat")]
    [InlineData(0, "kalan 40 dakika")]
    public void How_long_is_left_is_written_in_one_unit(int hoursAhead, string expected)
    {
        var deadline = hoursAhead == 0
            ? Now.AddMinutes(40)
            : Now.AddHours(hoursAhead);

        var message = Formatter.Compose(Notice(deadline), NotificationKind.NewNotice);

        Assert.Contains(expected, message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_deadline_that_has_passed_says_so()
    {
        var message = Formatter.Compose(Notice(Now.AddHours(-1)), NotificationKind.NewNotice);

        Assert.Contains("süre doldu", message.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(84, "🟢")]
    [InlineData(45, "🟡")]
    [InlineData(10, "🔴")]
    public void The_icon_follows_the_score(int score, string icon)
    {
        Assert.StartsWith(icon, Formatter.Compose(Notice(score: score), NotificationKind.NewNotice).Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_reminder_says_it_is_one()
    {
        var message = Formatter.Compose(Notice(), NotificationKind.DeadlineReminder);

        Assert.StartsWith("⏳", message.Text, StringComparison.Ordinal);
        Assert.Contains("HATIRLATMA", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_notice_with_no_cost_simply_has_no_cost_line()
    {
        var message = Formatter.Compose(Notice(cost: null), NotificationKind.NewNotice);

        Assert.DoesNotContain("💰", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_notice_with_no_documents_has_no_document_line()
    {
        Assert.DoesNotContain("📎", Formatter.Compose(Notice(), NotificationKind.NewNotice).Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_button_labels_come_from_the_config_file()
    {
        var message = Formatter.Compose(Notice(), NotificationKind.NewNotice);

        Assert.Equal("İncele", message.ReviewLabel);
        Assert.Equal("İlgilenmiyoruz", message.NotRelevantLabel);
    }

    [Fact]
    public void A_very_long_description_is_cut_rather_than_sent_whole()
    {
        var message = Formatter.Compose(
            Notice() with { Description = new string('a', 2000) },
            NotificationKind.NewNotice);

        Assert.True(message.Text.Length < 1000, $"message was {message.Text.Length} characters");
        Assert.Contains('…', message.Text);
    }

    [Fact]
    public void A_template_referring_to_a_field_nobody_supplies_does_not_throw()
    {
        // A typo in the config file should be visible in the message, not fatal
        // at three in the morning.
        var templates = MessageTemplates.Load("""
            {
              "notice": { "header": "{score} {typo} {title}", "deadline_unknown": "yok" },
              "buttons": { "review": "a", "not_relevant": "b" },
              "remaining": { "days": "{count} g", "hours": "{count} s", "minutes": "{count} d", "expired": "-" },
              "score_icons": [ { "at_least": 0, "icon": "*" } ]
            }
            """);

        var message = new NoticeMessageFormatter(new Fixed<MessageTemplates>(templates), Time)
            .Compose(Notice(), NotificationKind.NewNotice);

        Assert.Contains("{typo}", message.Text, StringComparison.Ordinal);
    }
}
