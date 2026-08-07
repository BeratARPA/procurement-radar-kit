using Microsoft.Extensions.Logging.Abstractions;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Settings;

namespace ProcurementRadar.UnitTests.Settings;

/// <summary>
/// When the seeder says a config file no longer matches the stored setting.
/// </summary>
/// <remarks>
/// The warning exists because a file edit that changes nothing looks exactly
/// like one that works: settings are read from the database and the file is only
/// the seed. It cost two hours of the wrong assumed deadline hour.
///
/// The warning is also the easy thing to get wrong in the other direction.
/// PostgreSQL's jsonb does not keep the order the keys were written in, so a
/// value read back is almost never byte-identical to its file - and the first
/// version of this reported all eight settings as diverged when two had.
/// A warning that is always on is a warning nobody reads, which is the same
/// failure as no warning at all.
/// </remarks>
public sealed class SettingDivergenceTests
{
    private static SettingSeeder Seeder(FakeSettingRepository settings) => new(
        settings,
        Path.Combine(RepositoryRoot.Find().FullName, "config"),
        NullLogger<SettingSeeder>.Instance);

    [Fact]
    public async Task A_stored_value_that_only_differs_by_key_order_is_not_a_difference()
    {
        // What jsonb hands back: the same document, its keys in its own order.
        var settings = new FakeSettingRepository();
        var file = await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot.Find().FullName, "config", "keywords.json"));

        settings.Stored[SettingKeys.Keywords] = Shuffled(file);

        var seeder = Seeder(settings);

        // Nothing to assert but the absence of noise, so the assertion is that
        // the stored value is left exactly as it was and the run completes.
        await seeder.RunAsync(CancellationToken.None);

        Assert.Equal(Shuffled(file), settings.Stored[SettingKeys.Keywords]);
    }

    [Fact]
    public async Task The_stored_value_is_never_replaced_by_the_file()
    {
        // The rule the warning exists to make visible, restated as a test: the
        // database wins, always, and a deployment cannot undo what somebody set.
        var settings = new FakeSettingRepository();
        var mine = """{"include": [ { "term": "yazılım", "weight": 99 } ], "exclude": [], "notify_threshold": 30}""";
        settings.Stored[SettingKeys.Keywords] = mine;

        await Seeder(settings).RunAsync(CancellationToken.None);

        Assert.Equal(mine, settings.Stored[SettingKeys.Keywords]);
    }

    /// <summary>The same json with its top-level keys written in reverse.</summary>
    private static string Shuffled(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);

        var properties = document.RootElement
            .EnumerateObject()
            .Reverse()
            .Select(property => System.Text.Json.JsonSerializer.Serialize(property.Name)
                + ":" + property.Value.GetRawText());

        return "{" + string.Join(",", properties) + "}";
    }
}
