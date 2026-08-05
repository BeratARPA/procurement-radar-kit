using Microsoft.Extensions.Time.Testing;
using ProcurementRadar.Collector;

namespace ProcurementRadar.UnitTests.Fetching;

public sealed class PollIntervalCalculatorTests
{
    // Turkey has been on UTC+03 all year round since 2016, so 02:00 local is
    // 23:00 UTC the day before.
    private static DateTimeOffset Istanbul(int year, int month, int day, int hour, int minute = 0)
        => new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(3)).ToUniversalTime();

    [Theory]
    [InlineData(1, 59, 15)]
    [InlineData(2, 0, 45)]
    [InlineData(3, 30, 45)]
    [InlineData(4, 59, 45)]
    [InlineData(5, 0, 15)]
    [InlineData(12, 0, 15)]
    [InlineData(23, 0, 15)]
    public void The_interval_triples_between_two_and_five_in_the_morning(
        int hour,
        int minute,
        int expectedMinutes)
    {
        var time = new FakeTimeProvider(Istanbul(2026, 3, 10, hour, minute));
        var calculator = new PollIntervalCalculator(time);

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), calculator.Effective(15));
    }

    [Fact]
    public void The_window_follows_Istanbul_and_not_the_machine()
    {
        // 03:00 in Istanbul is midnight UTC. A server running on UTC would see
        // this as the middle of the night before, and a server in Berlin as 01:00.
        // Only the Istanbul reading is the maintenance window.
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero));
        var calculator = new PollIntervalCalculator(time);

        Assert.True(calculator.IsNightWindow());
    }

    [Fact]
    public void A_source_that_has_never_been_fetched_is_due()
    {
        var time = new FakeTimeProvider(Istanbul(2026, 3, 10, 12));
        var calculator = new PollIntervalCalculator(time);

        Assert.True(calculator.IsDue(lastFetchAt: null, pollIntervalMinutes: 15));
    }

    [Fact]
    public void A_source_becomes_due_once_its_interval_has_passed()
    {
        var now = Istanbul(2026, 3, 10, 12);
        var time = new FakeTimeProvider(now);
        var calculator = new PollIntervalCalculator(time);

        Assert.False(calculator.IsDue(now.AddMinutes(-14), 15));
        Assert.True(calculator.IsDue(now.AddMinutes(-15), 15));
    }

    [Fact]
    public void At_night_a_source_waits_three_times_as_long()
    {
        var now = Istanbul(2026, 3, 10, 3);
        var time = new FakeTimeProvider(now);
        var calculator = new PollIntervalCalculator(time);

        Assert.False(calculator.IsDue(now.AddMinutes(-44), 15));
        Assert.True(calculator.IsDue(now.AddMinutes(-45), 15));
    }
}
