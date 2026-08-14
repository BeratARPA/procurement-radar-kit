using System.Text.Json;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Health;
using ProcurementRadar.Notifier;

namespace ProcurementRadar.UnitTests.Notifications;

/// <summary>
/// Every alarm the code can raise has something to say.
/// </summary>
/// <remarks>
/// On 14 August 2026 `never_produced` fired 26 times between 14:51 and 18:10 and
/// reached nobody. The enum had the kind, `HealthMonitor` raised it, the C#
/// template property existed with `= string.Empty` as its default - and
/// `tr-messages.json` had no key for it. So the alarm was assembled from an
/// empty string, Telegram answered "Bad Request: message text is empty", and the
/// row went into `health_alert` anyway, starting a cooldown for a message that
/// was never delivered.
///
/// The alarm was about the sources that have never produced a single notice,
/// which is the one condition nothing else in the system reports.
///
/// CLAUDE.md already lists "an alarm shipped with no message text" from an
/// earlier round. Twice is a pattern, and the pattern is that a default of empty
/// string turns a missing template into silence rather than a failure. Two
/// checks now: the loader refuses the document, and this ties the enum to the
/// file so a new alarm kind cannot be added without text.
/// </remarks>
public sealed class AlertTemplateTests
{
    private static string RealMessages() => File.ReadAllText(
        Path.Combine(RepositoryRoot.Find().FullName, "config", "tr-messages.json"));

    [Fact]
    public void The_shipped_document_has_text_for_every_alert()
    {
        var templates = MessageTemplates.Load(RealMessages());

        Assert.False(string.IsNullOrWhiteSpace(templates.Alerts.NeverProduced));
        Assert.False(string.IsNullOrWhiteSpace(templates.Alerts.FetchFailures));
        Assert.False(string.IsNullOrWhiteSpace(templates.Alerts.SourceDeactivated));
        Assert.False(string.IsNullOrWhiteSpace(templates.Alerts.SourceRecovered));
        Assert.False(string.IsNullOrWhiteSpace(templates.Alerts.ParserBroken));
        Assert.False(string.IsNullOrWhiteSpace(templates.Alerts.SilentSource));
        Assert.False(string.IsNullOrWhiteSpace(templates.Alerts.NothingCollected));
        Assert.False(string.IsNullOrWhiteSpace(templates.Alerts.PipelineStalled));
    }

    [Fact]
    public void Every_alert_kind_the_enum_declares_has_a_key_in_the_document()
    {
        // The half that would have caught this before it shipped. A kind added
        // to the enum with no key here fails in dotnet test rather than in a
        // Telegram 400 nobody is reading.
        //
        // WeeklySummary is not an alert - it has its own section with a header
        // and lines - so it is the one exclusion.
        var document = JsonDocument.Parse(RealMessages());
        var alerts = document.RootElement.GetProperty("alerts");

        var missing = Enum.GetValues<HealthAlertKind>()
            .Where(kind => kind != HealthAlertKind.WeeklySummary)
            .Select(EnumText.ToText)
            .Where(key => !alerts.TryGetProperty(key, out var text)
                || string.IsNullOrWhiteSpace(text.GetString()))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "alerts in tr-messages.json has no text for: " + string.Join(", ", missing));
    }

    [Fact]
    public void A_document_missing_an_alert_is_refused_rather_than_loaded_empty()
    {
        // What made it silent: every template property defaults to an empty
        // string, so a missing key parsed cleanly and the alarm sent nothing.
        var without = RealMessages().Replace("\"never_produced\"", "\"never_produced_x\"", StringComparison.Ordinal);

        var thrown = Assert.Throws<InvalidOperationException>(() => MessageTemplates.Load(without));

        Assert.Contains("never_produced", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_never_produced_text_says_which_source_it_is_about()
    {
        // An alarm naming no source is an alarm nobody can act on, and this one
        // fires for several sources at once.
        Assert.Contains(
            "{authority}",
            MessageTemplates.Load(RealMessages()).Alerts.NeverProduced,
            StringComparison.Ordinal);
    }
}
