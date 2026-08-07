using Microsoft.Extensions.Logging.Abstractions;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Settings;

namespace ProcurementRadar.UnitTests.Settings;

public sealed class SettingSeederTests
{
    private static string ConfigDirectory()
        => Path.Combine(RepositoryRoot.Find().FullName, "config");

    private static SettingSeeder Seeder(FakeSettingRepository settings) => new(
        settings,
        ConfigDirectory(),
        NullLogger<SettingSeeder>.Instance);

    [Fact]
    public async Task An_empty_database_is_filled_from_the_config_files()
    {
        var settings = new FakeSettingRepository();

        var seeded = await Seeder(settings).RunAsync(CancellationToken.None);

        Assert.Equal(SettingKeys.Seeds.Count, seeded);
        Assert.All(SettingKeys.Seeds, seed => Assert.True(settings.Stored.ContainsKey(seed.Key)));
    }

    [Fact]
    public async Task Every_declared_seed_file_exists()
    {
        // The list and the files are edited by different hands at different
        // times. A key naming a file nobody kept is a setting that seeds as
        // nothing and reads back as nothing.
        var settings = new FakeSettingRepository();

        await Seeder(settings).RunAsync(CancellationToken.None);

        Assert.All(
            SettingKeys.Seeds,
            seed => Assert.True(
                File.Exists(Path.Combine(ConfigDirectory(), seed.FileName)),
                $"config/{seed.FileName} is declared as the seed for '{seed.Key}' and is not there."));
    }

    [Fact]
    public async Task A_stored_setting_is_not_overwritten_by_its_file()
    {
        // The whole direction of D-16 in one test: a value set from the settings
        // screen survives a deployment that ships a different config file.
        var settings = new FakeSettingRepository();
        settings.Stored[SettingKeys.Keywords] = """{"include":[],"exclude":[],"notify_threshold":90}""";

        var seeded = await Seeder(settings).RunAsync(CancellationToken.None);

        Assert.Equal(SettingKeys.Seeds.Count - 1, seeded);
        Assert.Contains("90", settings.Stored[SettingKeys.Keywords], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Seeding_twice_changes_nothing_the_second_time()
    {
        var settings = new FakeSettingRepository();
        var seeder = Seeder(settings);

        await seeder.RunAsync(CancellationToken.None);
        var again = await seeder.RunAsync(CancellationToken.None);

        Assert.Equal(0, again);
    }
}

internal sealed class FakeSettingRepository : ISettingRepository
{
    public Dictionary<string, string> Stored { get; } = [];

    public List<(string Key, string? By)> Saved { get; } = [];

    public Task<IReadOnlyList<Setting>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Setting>>(
            Stored.Select(pair => new Setting { Key = pair.Key, Value = pair.Value }).ToList());

    public Task<Setting?> GetAsync(string key, CancellationToken cancellationToken)
        => Task.FromResult(Stored.TryGetValue(key, out var value)
            ? new Setting { Key = key, Value = value }
            : null);

    public Task<DateTimeOffset?> GetVersionAsync(CancellationToken cancellationToken)
        => Task.FromResult<DateTimeOffset?>(null);

    public Task SaveAsync(string key, string json, string? updatedBy, CancellationToken cancellationToken)
    {
        Stored[key] = json;
        Saved.Add((key, updatedBy));

        return Task.CompletedTask;
    }

    public Task<bool> InsertIfMissingAsync(string key, string json, CancellationToken cancellationToken)
    {
        if (Stored.ContainsKey(key))
        {
            return Task.FromResult(false);
        }

        Stored[key] = json;

        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<Setting>> ListHistoryAsync(
        string key,
        int limit,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Setting>>([]);
}
