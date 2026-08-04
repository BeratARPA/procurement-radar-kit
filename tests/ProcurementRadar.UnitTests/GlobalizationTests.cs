namespace ProcurementRadar.UnitTests;

public sealed class GlobalizationTests
{
    // D-08 converts Turkish source pages from Europe/Istanbul to UTC, and that
    // lookup only works while ICU data ships with the app. If somebody turns on
    // InvariantGlobalization in Directory.Build.props the build still succeeds
    // and every bid deadline silently shifts by three hours. This test is the
    // tripwire for that.
    [Fact]
    public void Europe_Istanbul_resolves_to_a_fixed_three_hour_offset()
    {
        var istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

        // Turkey abandoned daylight saving time in 2016 and has stayed on UTC+03
        // all year round since then.
        var midWinter = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var midSummer = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(TimeSpan.FromHours(3), istanbul.GetUtcOffset(midWinter));
        Assert.Equal(TimeSpan.FromHours(3), istanbul.GetUtcOffset(midSummer));
    }
}
