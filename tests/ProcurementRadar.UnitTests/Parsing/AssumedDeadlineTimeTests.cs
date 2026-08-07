using Microsoft.Extensions.Logging.Abstractions;
using ProcurementRadar.Domain.Settings;
using ProcurementRadar.Parsers;

namespace ProcurementRadar.UnitTests.Parsing;

/// <summary>
/// What happens when a notice gives a date and no hour.
/// </summary>
/// <remarks>
/// It used to become 23:59 - the end of the calendar day. Authorities close at
/// the end of the working day, so that was the dangerous direction to guess in:
/// it told the reader they still had the evening, and it pushed the
/// six-hours-left reminder to 17:59, after the real cut-off.
///
/// 17:00 now, and the guess travels with the notice so the message can admit to
/// it. A guess presented as a deadline is worse than a guess owned up to.
/// </remarks>
public sealed class AssumedDeadlineTimeTests
{
    private static readonly TurkishDatePatterns Patterns = TurkishDatePatterns.Load(
        File.ReadAllText(Path.Combine(
            RepositoryRoot.Find().FullName, "config", "tr-date-patterns.json")));

    private static TurkishDateParser Parser() => new(
        new Fixed<TurkishDatePatterns>(Patterns),
        NullLogger<TurkishDateParser>.Instance);

    [Fact]
    public void A_date_with_no_hour_lands_at_the_end_of_the_working_day()
    {
        var reading = Parser().Read("12.03.2026");

        // March, so Istanbul is UTC+3 and 17:00 there is 14:00 here.
        Assert.Equal(new DateTimeOffset(2026, 3, 12, 14, 0, 0, TimeSpan.Zero), reading.Value);
        Assert.True(reading.TimeWasAssumed);
    }

    [Fact]
    public void The_guess_is_never_later_than_the_end_of_a_working_day()
    {
        // The guard on the direction of the guess. Raising this back towards
        // midnight hands the reader hours that are not there, and moves the
        // reminder past the moment it exists to beat.
        var reading = Parser().Read("12.03.2026");

        var istanbul = TimeZoneInfo.ConvertTime(
            reading.Value!.Value,
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"));

        Assert.True(
            istanbul.Hour <= 17,
            $"A deadline nobody wrote an hour for was put at {istanbul.Hour}:00.");
    }

    [Fact]
    public void An_hour_that_was_written_down_is_not_a_guess()
    {
        var reading = Parser().Read("12.03.2026 saat 10:00");

        Assert.False(reading.TimeWasAssumed);
        Assert.Equal(new DateTimeOffset(2026, 3, 12, 7, 0, 0, TimeSpan.Zero), reading.Value);
    }

    [Fact]
    public void Nothing_readable_assumes_nothing()
    {
        var reading = Parser().Read("teklifler elden teslim edilecektir");

        Assert.Null(reading.Value);
        Assert.False(reading.TimeWasAssumed);
    }
}
