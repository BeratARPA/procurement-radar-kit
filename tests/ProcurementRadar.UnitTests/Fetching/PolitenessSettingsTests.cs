using ProcurementRadar.Api;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Settings;

namespace ProcurementRadar.UnitTests.Fetching;

/// <summary>
/// The limits a settings screen must not be able to cross.
/// </summary>
/// <remarks>
/// D-16 made the politeness budget editable and kept the floors on the server.
/// The reasoning is CLAUDE.md's and it is not about tidiness: there is no speed
/// to be gained from asking a public authority's web site more often, and there
/// is an IP to be lost. One of these sources already answers this project with
/// 403, and one is out of bounds by its own robots.txt.
///
/// Both doors are tested. The api refuses the value, and if it reaches the
/// database by some other route the worker refuses to load it and keeps what it
/// had.
/// </remarks>
public sealed class PolitenessSettingsTests
{
    private const string Sound = """
        {
          "minimum_host_interval_seconds": 5,
          "maximum_jitter_seconds": 3,
          "respect_robots": true,
          "night_window_start_hour": 2,
          "night_window_end_hour": 5,
          "night_interval_multiplier": 3,
          "request_timeout_seconds": 30,
          "maximum_attempts": 3
        }
        """;

    private static string With(string field, string value)
        => Sound.Replace($"\"{field}\": " + (field == "respect_robots" ? "true" : Original(field)),
            $"\"{field}\": {value}",
            StringComparison.Ordinal);

    private static string Original(string field) => field switch
    {
        "minimum_host_interval_seconds" => "5",
        "maximum_jitter_seconds" => "3",
        "night_interval_multiplier" => "3",
        "maximum_attempts" => "3",
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    [Fact]
    public void The_shipped_file_is_acceptable_to_its_own_rules()
    {
        var json = File.ReadAllText(
            Path.Combine(RepositoryRoot.Find().FullName, "config", "politeness.json"));

        var settings = PolitenessSettings.Load(json);

        Assert.Equal(5, settings.MinimumHostIntervalSeconds);
        Assert.True(settings.RespectRobots);
    }

    [Theory]
    [InlineData("4")]
    [InlineData("1")]
    [InlineData("0")]
    public void Below_five_seconds_between_requests_is_refused(string seconds)
    {
        var json = With("minimum_host_interval_seconds", seconds);

        Assert.Throws<InvalidOperationException>(() => PolitenessSettings.Load(json));
        Assert.NotNull(SettingValidator.Validate(SettingKeys.Politeness, json));
    }

    [Fact]
    public void Turning_robots_off_is_refused()
    {
        var json = With("respect_robots", "false");

        Assert.Throws<InvalidOperationException>(() => PolitenessSettings.Load(json));
        Assert.NotNull(SettingValidator.Validate(SettingKeys.Politeness, json));
    }

    [Fact]
    public void Polling_harder_at_night_is_refused()
    {
        // Below one inverts the night rule rather than relaxing it: it would
        // poll more often during the hours the maintenance windows are in.
        Assert.Throws<InvalidOperationException>(
            () => PolitenessSettings.Load(With("night_interval_multiplier", "0")));
    }

    [Fact]
    public void Every_refusal_is_reported_at_once()
    {
        // Somebody correcting a form should not have to submit it four times to
        // discover what else is wrong with it.
        var json = Sound
            .Replace("\"minimum_host_interval_seconds\": 5", "\"minimum_host_interval_seconds\": 1", StringComparison.Ordinal)
            .Replace("\"respect_robots\": true", "\"respect_robots\": false", StringComparison.Ordinal);

        var problem = SettingValidator.Validate(SettingKeys.Politeness, json);

        Assert.NotNull(problem);
        Assert.Contains("minimum_host_interval_seconds", problem, StringComparison.Ordinal);
        Assert.Contains("respect_robots", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_longer_gap_than_the_floor_is_allowed()
    {
        // The floor is a floor, not a value. Slowing down is always permitted.
        var settings = PolitenessSettings.Load(With("minimum_host_interval_seconds", "30"));

        Assert.Equal(30, settings.MinimumHostIntervalSeconds);
    }
}
