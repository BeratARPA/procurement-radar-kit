using Microsoft.Extensions.Logging.Abstractions;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Normalization;
using ProcurementRadar.Domain.Settings;
using ProcurementRadar.Scoring;
using ProcurementRadar.UnitTests.Normalization;

namespace ProcurementRadar.UnitTests.Settings;

/// <summary>
/// The point of D-16 in one place: a setting changed while the worker is running
/// takes effect without restarting it.
/// </summary>
public sealed class LiveSettingTests
{
    // Not an empty list: KeywordRules.Load refuses one, on the grounds that a
    // filter matching nothing is indistinguishable from a broken system. So the
    // "before" list is one term that this title does not contain.
    private const string NoTerms = """
        {"include": [ { "term": "asansör bakım", "weight": 40 } ], "exclude": [], "notify_threshold": 30}
        """;

    private const string OneTerm = """
        {"include": [ { "term": "yazılım", "weight": 40 } ], "exclude": [], "notify_threshold": 30}
        """;

    private static LiveSetting<KeywordRules> Live(string json) => new(
        SettingKeys.Keywords,
        KeywordRules.Load,
        KeywordRules.Load(json));

    private static RuleScorer Scorer(LiveSetting<KeywordRules> keywords)
        => new(keywords, new NoticeNormalizer(NoticeNormalizerTests.Rules));

    [Fact]
    public void A_scorer_built_before_the_change_scores_by_the_list_after_it()
    {
        // The scorer is constructed once, as a singleton is, and never told that
        // anything happened. Adding a keyword from the settings screen has to
        // reach it anyway, or the screen is decoration.
        var keywords = Live(NoTerms);
        var scorer = Scorer(keywords);

        Assert.Equal(0, scorer.Score("Yazılım Bakım ve Destek Hizmeti Alımı").Value);

        Assert.True(keywords.TryApply(OneTerm, out _));

        Assert.Equal(40, scorer.Score("Yazılım Bakım ve Destek Hizmeti Alımı").Value);
    }

    [Fact]
    public void A_setting_that_will_not_load_leaves_the_previous_one_in_place()
    {
        // Somebody types into a form and saves something that is not a keyword
        // list. Running on yesterday's list is working; running on none is not.
        var keywords = Live(OneTerm);
        var scorer = Scorer(keywords);

        Assert.False(keywords.TryApply("{ this is not json", out var problem));
        Assert.NotNull(problem);

        Assert.Equal(40, scorer.Score("Yazılım Bakım ve Destek Hizmeti Alımı").Value);
    }

    [Fact]
    public async Task Nothing_is_read_when_nothing_changed()
    {
        // The version is asked for every thirty seconds, for ever. It must not
        // drag the whole settings table along with it each time.
        var repository = new CountingSettingRepository { Version = DateTimeOffset.UnixEpoch };
        var keywords = Live(NoTerms);
        var refresher = new SettingsRefresher(
            repository,
            [keywords],
            NullLogger<SettingsRefresher>.Instance);

        await refresher.RunAsync(CancellationToken.None);
        await refresher.RunAsync(CancellationToken.None);
        await refresher.RunAsync(CancellationToken.None);

        Assert.Equal(3, repository.VersionReads);
        Assert.Equal(1, repository.ListReads);
    }

    [Fact]
    public async Task A_new_version_reaches_the_setting()
    {
        var repository = new CountingSettingRepository
        {
            Version = DateTimeOffset.UnixEpoch,
            Stored = { [SettingKeys.Keywords] = OneTerm }
        };

        var keywords = Live(NoTerms);
        var refresher = new SettingsRefresher(
            repository,
            [keywords],
            NullLogger<SettingsRefresher>.Instance);

        Assert.Equal(1, await refresher.RunAsync(CancellationToken.None));
        Assert.Equal("yazılım", Assert.Single(keywords.Value.Include).Term);
    }
}

internal sealed class CountingSettingRepository : ISettingRepository
{
    public DateTimeOffset? Version { get; set; }

    public Dictionary<string, string> Stored { get; } = [];

    public int VersionReads { get; private set; }

    public int ListReads { get; private set; }

    public Task<DateTimeOffset?> GetVersionAsync(CancellationToken cancellationToken)
    {
        VersionReads++;

        return Task.FromResult(Version);
    }

    public Task<IReadOnlyList<Setting>> ListAsync(CancellationToken cancellationToken)
    {
        ListReads++;

        return Task.FromResult<IReadOnlyList<Setting>>(
            Stored.Select(pair => new Setting { Key = pair.Key, Value = pair.Value }).ToList());
    }

    public Task<Setting?> GetAsync(string key, CancellationToken cancellationToken)
        => Task.FromResult<Setting?>(null);

    public Task SaveAsync(string key, string json, string? updatedBy, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<bool> InsertIfMissingAsync(string key, string json, CancellationToken cancellationToken)
        => Task.FromResult(false);

    public Task<IReadOnlyList<Setting>> ListHistoryAsync(
        string key,
        int limit,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Setting>>([]);
}
