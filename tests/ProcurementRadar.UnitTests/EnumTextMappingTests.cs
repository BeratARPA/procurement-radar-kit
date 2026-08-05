using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Persistence;

namespace ProcurementRadar.UnitTests;

/// <summary>
/// The enum-to-text mapping is derived from member names rather than listed, so
/// each of these tests pins a whole enum to the exact strings the schema stores.
/// Renaming a member or adding one now fails here instead of writing a value the
/// database has never seen.
/// </summary>
public sealed class EnumTextMappingTests
{
    [Fact]
    public void Authority_type_maps_to_the_values_in_the_schema()
    {
        Assert.Equal(
            new[]
            {
                "university", "municipality", "provincial_directorate",
                "hospital", "soe_subsidiary", "ekap"
            },
            DatabaseTextOf<AuthorityType>());
    }

    [Fact]
    public void Procurement_type_maps_to_the_values_in_the_schema()
    {
        Assert.Equal(
            new[] { "direct_procurement", "tender", "exempt" },
            DatabaseTextOf<ProcurementType>());
    }

    [Fact]
    public void Notice_status_maps_to_the_values_in_the_schema()
    {
        Assert.Equal(
            new[] { "new", "reviewed", "bid_submitted", "missed", "not_relevant" },
            DatabaseTextOf<NoticeStatus>());
    }

    [Fact]
    public void Notification_channel_maps_to_the_values_in_the_schema()
    {
        Assert.Equal(new[] { "telegram" }, DatabaseTextOf<NotificationChannel>());
    }

    [Fact]
    public void Every_member_survives_a_round_trip()
    {
        AssertRoundTrips<AuthorityType>();
        AssertRoundTrips<ProcurementType>();
        AssertRoundTrips<NoticeStatus>();
        AssertRoundTrips<NotificationChannel>();
    }

    [Fact]
    public void A_value_the_code_does_not_model_fails_loudly()
    {
        var handler = new SnakeCaseEnumTypeHandler<NoticeStatus>();

        var exception = Assert.Throws<InvalidOperationException>(() => handler.Parse("archived"));

        Assert.Contains("archived", exception.Message, StringComparison.Ordinal);
    }

    private static string[] DatabaseTextOf<T>() where T : struct, Enum
        => Enum.GetValues<T>().Select(SnakeCaseEnumTypeHandler<T>.ToDatabaseText).ToArray();

    private static void AssertRoundTrips<T>() where T : struct, Enum
    {
        var handler = new SnakeCaseEnumTypeHandler<T>();

        foreach (var value in Enum.GetValues<T>())
        {
            Assert.Equal(value, handler.Parse(SnakeCaseEnumTypeHandler<T>.ToDatabaseText(value)));
        }
    }
}
