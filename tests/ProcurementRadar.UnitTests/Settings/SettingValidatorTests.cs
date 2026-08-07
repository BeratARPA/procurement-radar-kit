using ProcurementRadar.Api;
using ProcurementRadar.Domain.Settings;

namespace ProcurementRadar.UnitTests.Settings;

/// <summary>
/// What the api refuses to store.
/// </summary>
/// <remarks>
/// The worker survives a bad setting - it logs an error and keeps the previous
/// value - but surviving is not being told. This is the half that answers the
/// person who pressed save, while they are still looking at the screen.
/// </remarks>
public sealed class SettingValidatorTests
{
    [Fact]
    public void Every_setting_the_system_has_can_be_validated()
    {
        // A key the seeder fills but the validator does not know would be
        // saveable as anything at all.
        Assert.All(SettingKeys.Seeds, seed => Assert.True(SettingValidator.IsKnown(seed.Key)));
    }

    [Fact]
    public void A_key_nobody_reads_is_refused()
    {
        Assert.NotNull(SettingValidator.Validate("esik", """{"value": 30}"""));
    }

    [Theory]
    [InlineData("{ not json at all")]
    [InlineData("")]
    public void Something_that_is_not_json_is_refused(string json)
    {
        Assert.NotNull(SettingValidator.Validate(SettingKeys.Keywords, json));
    }

    [Fact]
    public void A_keyword_list_with_no_terms_is_refused()
    {
        // KeywordRules.Load has always refused this, on the grounds that a filter
        // matching nothing cannot be told apart from a broken system. Saving it
        // from a phone must fail the same way.
        var problem = SettingValidator.Validate(
            SettingKeys.Keywords,
            """{"include": [], "exclude": [], "notify_threshold": 30}""");

        Assert.NotNull(problem);
    }

    [Fact]
    public void A_usable_keyword_list_is_accepted()
    {
        Assert.Null(SettingValidator.Validate(
            SettingKeys.Keywords,
            """{"include": [ { "term": "yazılım", "weight": 40 } ], "exclude": [], "notify_threshold": 30}"""));
    }

    [Fact]
    public void The_real_config_files_pass_their_own_validator()
    {
        // The files are what a fresh install is seeded from. If one of them would
        // be refused by the api, the settings screen could not save it back
        // unchanged - and somebody would find that out by trying.
        var configDirectory = Path.Combine(RepositoryRoot.Find().FullName, "config");

        Assert.All(SettingKeys.Seeds, seed =>
        {
            var json = File.ReadAllText(Path.Combine(configDirectory, seed.FileName));

            Assert.Null(SettingValidator.Validate(seed.Key, json));
        });
    }
}
