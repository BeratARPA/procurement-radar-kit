using ProcurementRadar.Domain.Enums;

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
    public void A_value_the_code_does_not_model_is_refused()
    {
        Assert.False(EnumText.TryParse<NoticeStatus>("archived", out _));
    }

    [Fact]
    public void The_stored_text_turns_back_into_the_member_name_the_way_the_SELECT_does()
    {
        // Dapper reads an enum column with Enum.Parse against the member names,
        // so every SELECT converts the stored snake_case first. This is that
        // conversion, in C#, applied to every member of every mapped enum: if
        // the two ever disagree, reading a notice throws.
        AssertMemberNamesSurvive<AuthorityType>();
        AssertMemberNamesSurvive<ProcurementType>();
        AssertMemberNamesSurvive<NoticeStatus>();
        AssertMemberNamesSurvive<NotificationChannel>();
    }

    /// <summary>
    /// The C# equivalent of replace(initcap(replace(x, '_', ' ')), ' ', '').
    /// </summary>
    private static string AsPostgresWould(string storedText)
        => string.Concat(storedText
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));

    private static void AssertMemberNamesSurvive<T>() where T : struct, Enum
    {
        foreach (var value in Enum.GetValues<T>())
        {
            Assert.Equal(value.ToString(), AsPostgresWould(EnumText.ToText(value)));
        }
    }

    private static string[] DatabaseTextOf<T>() where T : struct, Enum
        => Enum.GetValues<T>().Select(EnumText.ToText).ToArray();

    private static void AssertRoundTrips<T>() where T : struct, Enum
    {
        foreach (var value in Enum.GetValues<T>())
        {
            Assert.True(EnumText.TryParse<T>(EnumText.ToText(value), out var parsed));
            Assert.Equal(value, parsed);
        }
    }
}
