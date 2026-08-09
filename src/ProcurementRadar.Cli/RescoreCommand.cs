using ProcurementRadar.Domain.Normalization;
using ProcurementRadar.Domain.Settings;
using ProcurementRadar.Persistence;
using ProcurementRadar.Scoring;
using Serilog;

namespace ProcurementRadar.Cli;

/// <summary>
/// Score everything already collected with the keyword list as it stands.
/// </summary>
/// <remarks>
/// A notice is scored once, at parse time. Change the word list and the change
/// only ever reaches notices that arrive afterwards - the archive keeps the
/// verdict of a list that no longer exists. That was measured: the day 26 terms
/// were added, the CRM and OCR tenders the new terms match still read 0.
///
/// Not the same as `reparse`. That one re-reads the archived pages and rebuilds
/// notices, which is the answer when a parser was wrong. This one touches
/// nothing but `notice_score`, which is the answer when the filter changed.
/// </remarks>
internal static class RescoreCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var connectionString = CliConfiguration.ConnectionString();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Log.Error("PR_DB_CONNECTION_STRING is not set");
            return 1;
        }

        var dryRun = args.Contains("--dry-run", StringComparer.Ordinal);

        PersistenceSetup.RegisterTypeHandlers();

        await using var connectionFactory = new NpgsqlConnectionFactory(connectionString);
        var settings = new SettingRepository(connectionFactory, TimeProvider.System);

        // From the table, not from config/keywords.json. The file is only what an
        // empty table is seeded from (D-16), so scoring the archive from the file
        // would score it with a list the worker is not using - which is the exact
        // failure CLAUDE.md records under "a config file / a row in setting".
        var storedKeywords = await settings
            .GetAsync(SettingKeys.Keywords, cancellationToken)
            .ConfigureAwait(false);

        if (storedKeywords is null)
        {
            Log.Error(
                "Setting '{Key}' is not in the table. Run the worker or the seeder once first",
                SettingKeys.Keywords);
            return 1;
        }

        var storedNormalization = await settings
            .GetAsync(SettingKeys.Normalization, cancellationToken)
            .ConfigureAwait(false);

        if (storedNormalization is null)
        {
            Log.Error("Setting '{Key}' is not in the table", SettingKeys.Normalization);
            return 1;
        }

        var rules = KeywordRules.Load(storedKeywords.Value);
        var normalizer = new NoticeNormalizer(NormalizationRules.Load(storedNormalization.Value));
        var scorer = new RuleScorer(new Fixed<KeywordRules>(rules), normalizer);

        Log.Information(
            "Rescoring with {Include} include term(s), {Exclude} exclude term(s), threshold {Threshold}",
            rules.Include.Count,
            rules.Exclude.Count,
            rules.NotifyThreshold);

        var queries = new CommandQueries(connectionFactory);
        var scores = new NoticeScoreRepository(connectionFactory, TimeProvider.System);

        var rescorer = new NoticeRescorer(
            queries,
            // --dry-run answers "what would move" without moving it. The same
            // question the filter preview answers, asked against the stored list
            // rather than a proposed one.
            dryRun ? new DiscardingScoreRepository() : scores,
            TimeProvider.System);

        var result = await rescorer.RescoreAsync(scorer, int.MaxValue, cancellationToken).ConfigureAwait(false);

        foreach (var notice in result.Notable)
        {
            Log.Information(
                "{Before} -> {After}  #{NoticeId} {Title}",
                notice.Before,
                notice.After,
                notice.NoticeId,
                notice.Title);
        }

        // Logged at WARN and separately from the movers above. A notice that
        // stopped qualifying is the half of a keyword change nobody goes looking
        // for, and the list above is ordered so that it scrolls off the end.
        foreach (var notice in result.Dropped)
        {
            Log.Warning(
                "No longer above the threshold: {Before} -> {After}  #{NoticeId} {Title}",
                notice.Before,
                notice.After,
                notice.NoticeId,
                notice.Title);
        }

        Log.Information(
            "{Verb} {Considered} notice(s): {Changed} changed, {Gained} now above the threshold of "
            + "{Threshold}, {Lost} now below it",
            dryRun ? "Would have rescored" : "Rescored",
            result.Considered,
            result.Changed,
            result.Gained,
            result.Threshold,
            result.Lost);

        return 0;
    }

    /// <summary>Accepts every write and keeps none, so --dry-run reports without changing.</summary>
    private sealed class DiscardingScoreRepository : Domain.Repositories.INoticeScoreRepository
    {
        public Task UpsertAsync(Domain.Entities.NoticeScore score, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
