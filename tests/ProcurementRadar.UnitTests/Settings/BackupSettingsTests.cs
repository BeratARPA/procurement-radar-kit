using ProcurementRadar.Domain.Settings;

namespace ProcurementRadar.UnitTests.Settings;

/// <summary>
/// The backup setting, and the two numbers that must not be zero.
/// </summary>
/// <remarks>
/// D-24. The destination is the part that changes per machine, so it is a
/// setting rather than a constant - and a setting is something a person can
/// type a wrong number into, which is why the loader refuses rather than
/// clamps. A retention of zero deletes the dump it just wrote; an interval of
/// zero re-mirrors 418 MB on every run.
/// </remarks>
public sealed class BackupSettingsTests
{
    private static string Json(string body) => $$"""{{{body}}}""";

    [Fact]
    public void The_shipped_config_loads()
    {
        // The real file, not an invented one (D-09): this is what an empty
        // table is seeded from, so it is the shape that has to survive.
        var path = Path.Combine(RepositoryRoot.Find().FullName, "config", "backup.json");

        var loaded = BackupSettings.Load(File.ReadAllText(path));

        Assert.True(loaded.IsConfigured);
        Assert.True(loaded.KeepDumps >= 1);
        Assert.True(loaded.ArchiveEveryDays >= 1);
    }

    [Fact]
    public void An_empty_destination_means_backups_are_off_rather_than_broken()
    {
        // A machine with nowhere to write is a normal state - no USB disk today
        // - and the command says so and exits cleanly. Refusing to load would
        // turn "not set up yet" into a failing scheduled task every night.
        var loaded = BackupSettings.Load(Json("""
            "destination": "", "database": true, "keep_dumps": 14,
            "archive": true, "archive_every_days": 7
            """));

        Assert.False(loaded.IsConfigured);
    }

    [Fact]
    public void Keeping_zero_dumps_is_refused()
    {
        // It would delete the dump it had just written, and the folder would
        // look tidy and hold nothing.
        Assert.Throws<InvalidOperationException>(() => BackupSettings.Load(Json("""
            "destination": "D:\\x", "keep_dumps": 0, "archive_every_days": 7
            """)));
    }

    [Fact]
    public void A_zero_day_archive_interval_is_refused()
    {
        // Every run would copy the whole archive again. At 418 MB that is a
        // backup that spends its night rewriting what it wrote last night.
        Assert.Throws<InvalidOperationException>(() => BackupSettings.Load(Json("""
            "destination": "D:\\x", "keep_dumps": 14, "archive_every_days": 0
            """)));
    }

    [Fact]
    public void Missing_fields_fall_back_to_the_defaults_rather_than_throwing()
    {
        // A hand-written setting with only a destination in it is a reasonable
        // thing for somebody to write, and the rest has sensible answers.
        var loaded = BackupSettings.Load("""{ "destination": "D:\\x" }""");

        Assert.Equal("D:\\x", loaded.Destination);
        Assert.True(loaded.Database);
        Assert.True(loaded.Archive);
        Assert.Equal(14, loaded.KeepDumps);
        Assert.Equal(7, loaded.ArchiveEveryDays);
    }
}
