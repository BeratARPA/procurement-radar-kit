using Dapper;
using ProcurementRadar.Domain.Notifications;

namespace ProcurementRadar.Persistence;

/// <summary>Hand-written SQL behind the Telegram commands (D-01).</summary>
public sealed class CommandQueries : ICommandQueries
{
    private readonly IDbConnectionFactory _connectionFactory;

    public CommandQueries(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // The notice half of every list row, in one place. Written out five times it
    // was five places to forget the next column - which is exactly how
    // bid_deadline_time_assumed went missing from the upsert for two hours. The
    // score is left out because the open list joins it and the rest coalesce it.
    private const string NoticeListColumns = """
        n.id AS "NoticeId", n.title AS "Title", n.authority_name AS "AuthorityName",
               n.bid_deadline_at AS "BidDeadlineAt",
               n.procurement_type AS "ProcurementType", n.procedure_code AS "ProcedureCode"
        """;

    // Still new, still scored, and not already closed. A notice whose deadline has
    // passed is not open however high it scored, and showing it would waste the
    // one screen somebody is looking at.
    private const string ListOpen = $"""
        SELECT {NoticeListColumns},
               s.rule_score AS "Score"
        FROM procurement_notice n
        JOIN notice_score s ON s.notice_id = n.id
        WHERE n.status = 'new'
          AND s.rule_score >= @MinimumScore
          AND (n.bid_deadline_at IS NULL OR n.bid_deadline_at > now())
        ORDER BY n.bid_deadline_at ASC NULLS LAST, s.rule_score DESC
        LIMIT @Limit
        """;

    // Everything, newest first. LEFT JOIN because a notice with no score row is
    // still a notice - the open list can use an inner join because it filters on
    // the score anyway, and this one must not silently drop what has not been
    // scored yet.
    private const string ListAll = $"""
        SELECT {NoticeListColumns},
               coalesce(s.rule_score, 0) AS "Score"
        FROM procurement_notice n
        LEFT JOIN notice_score s ON s.notice_id = n.id
        WHERE coalesce(s.rule_score, 0) >= @MinimumScore
        ORDER BY n.first_seen_at DESC
        LIMIT @Limit
        """;

    private const string ListSeenSince = $"""
        SELECT {NoticeListColumns},
               coalesce(s.rule_score, 0) AS "Score"
        FROM procurement_notice n
        LEFT JOIN notice_score s ON s.notice_id = n.id
        WHERE n.first_seen_at >= @Since
        ORDER BY coalesce(s.rule_score, 0) DESC, n.first_seen_at DESC
        LIMIT @Limit
        """;

    private const string Status = """
        SELECT
            (SELECT count(*)::int FROM source WHERE is_active)                        AS "ActiveSources",
            (SELECT count(*)::int FROM source WHERE consecutive_failures > 0)         AS "FailingSources",
            (SELECT count(*)::int FROM procurement_notice)                            AS "Notices",
            (SELECT count(*)::int FROM procurement_notice
              WHERE first_seen_at >= now() - interval '24 hours')                     AS "NoticesToday",
            (SELECT count(*)::int FROM notification WHERE sent_at IS NOT NULL)        AS "Announced",
            (SELECT max(last_fetch_at) FROM source)                                   AS "LastFetchAt"
        """;

    public async Task<IReadOnlyList<CommandNotice>> ListOpenAsync(
        int minimumScore,
        int limit,
        CancellationToken cancellationToken)
        => await QueryAsync(ListOpen, new { MinimumScore = minimumScore, Limit = limit }, cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<CommandNotice>> ListAllAsync(
        int minimumScore,
        int limit,
        CancellationToken cancellationToken)
        => await QueryAsync(ListAll, new { MinimumScore = minimumScore, Limit = limit }, cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<CommandNotice>> ListSeenSinceAsync(
        DateTimeOffset since,
        int limit,
        CancellationToken cancellationToken)
        => await QueryAsync(ListSeenSince, new { Since = since.ToUniversalTime(), Limit = limit }, cancellationToken)
            .ConfigureAwait(false);

    public async Task<SystemStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        return await connection.QuerySingleAsync<SystemStatus>(
            new CommandDefinition(Status, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    // ILIKE rather than a text search index: a few hundred rows today and a few
    // thousand a year, where a sequential scan costs nothing and an index would
    // want a Turkish configuration PostgreS"""L does not ship.
    private const string Search = $"""
        SELECT {NoticeListColumns},
               coalesce(s.rule_score, 0) AS "Score"
        FROM procurement_notice n
        LEFT JOIN notice_score s ON s.notice_id = n.id
        WHERE n.title ILIKE @Pattern OR n.authority_name ILIKE @Pattern
        ORDER BY n.first_seen_at DESC
        LIMIT @Limit
        """;

    // One select, two keys. Written apart, the id copy would answer a slightly
    // different question within a release - the Telegram reply and the screen
    // would disagree about the same notice and neither would look wrong.
    private const string DetailSelect = """
        SELECT n.id AS "NoticeId", n.title AS "Title", n.authority_name AS "AuthorityName",
               n.ikn AS "Ikn", coalesce(s.rule_score, 0) AS "Score", s.rationale AS "Rationale",
               n.bid_deadline_at AS "BidDeadlineAt", n.source_url AS "SourceUrl",
               n.status AS "Status",
               n.procurement_type AS "ProcurementType", n.procedure_code AS "ProcedureCode",
               (SELECT d.extracted_text FROM notice_document d
                 WHERE d.notice_id = n.id AND d.extracted_text IS NOT NULL
                 ORDER BY length(d.extracted_text) DESC LIMIT 1) AS "AnnouncementText"
        FROM procurement_notice n
        LEFT JOIN notice_score s ON s.notice_id = n.id
        """;

    private const string Detail = $"""
        {DetailSelect}
        WHERE n.ikn = @Ikn
        LIMIT 1
        """;

    private const string DetailById = $"""
        {DetailSelect}
        WHERE n.id = @NoticeId
        LIMIT 1
        """;

    // Failing, or quiet long enough to be suspicious after having produced
    // something before. A source that never produced anything is a different
    // problem and carries its reason in its own notes.
    private const string TroubledSources = """
        SELECT authority_name AS "AuthorityName", consecutive_failures AS "ConsecutiveFailures",
               last_success_at AS "LastSuccessAt", last_new_notice_at AS "LastNewNoticeAt"
        FROM source
        WHERE is_active
          AND (consecutive_failures > 0
               OR (last_new_notice_at IS NOT NULL
                   AND last_new_notice_at < now() - make_interval(days => @SilentDays)))
        ORDER BY consecutive_failures DESC, last_new_notice_at ASC NULLS FIRST
        LIMIT 15
        """;

    private const string ScoreReport = """
        SELECT n.status AS "Status", count(*)::int AS "Count",
               round(avg(s.rule_score)::numeric, 1)::float8 AS "AverageScore",
               max(s.rule_score) AS "MaximumScore"
        FROM procurement_notice n
        JOIN notice_score s ON s.notice_id = n.id
        GROUP BY n.status
        ORDER BY count(*) DESC
        """;

    private const string ClosingSoon = $"""
        SELECT {NoticeListColumns},
               s.rule_score AS "Score"
        FROM procurement_notice n
        JOIN notice_score s ON s.notice_id = n.id
        WHERE n.status = 'new'
          AND n.bid_deadline_at BETWEEN now() AND now() + make_interval(hours => @WithinHours)
        ORDER BY n.bid_deadline_at ASC
        LIMIT @Limit
        """;

    public async Task<IReadOnlyList<CommandNotice>> SearchAsync(
        string term,
        int limit,
        CancellationToken cancellationToken)
        => await QueryAsync(Search, new { Pattern = $"%{term}%", Limit = limit }, cancellationToken)
            .ConfigureAwait(false);

    public async Task<NoticeDetail?> GetDetailAsync(string ikn, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        return await connection.QuerySingleOrDefaultAsync<NoticeDetail>(
            new CommandDefinition(Detail, new { Ikn = ikn }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<NoticeDetail?> GetDetailByIdAsync(long noticeId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        return await connection.QuerySingleOrDefaultAsync<NoticeDetail>(
            new CommandDefinition(DetailById, new { NoticeId = noticeId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SourceHealth>> ListTroubledSourcesAsync(
        int silentDays,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<SourceHealth>(
            new CommandDefinition(
                TroubledSources,
                new { SilentDays = silentDays },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    public async Task<IReadOnlyList<ScoreByStatus>> GetScoreReportAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<ScoreByStatus>(
            new CommandDefinition(ScoreReport, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    public async Task<IReadOnlyList<CommandNotice>> ListClosingSoonAsync(
        int withinHours,
        int limit,
        CancellationToken cancellationToken)
        => await QueryAsync(ClosingSoon, new { WithinHours = withinHours, Limit = limit }, cancellationToken)
            .ConfigureAwait(false);

    // No join and no score: a preview is asked about a filter that does not
    // exist yet, so the stored score is the wrong answer by definition.
    private const string ForPreview = """
        SELECT id AS "NoticeId", title AS "Title", authority_name AS "AuthorityName",
               description AS "Description", okas_codes AS "OkasCodes"
        FROM procurement_notice
        ORDER BY first_seen_at DESC
        LIMIT @Limit
        """;

    public async Task<IReadOnlyList<PreviewNotice>> ListForPreviewAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<PreviewNotice>(
            new CommandDefinition(ForPreview, new { Limit = limit }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    private const string ForRescore = """
        SELECT n.id AS "NoticeId", n.title AS "Title",
               n.description AS "Description", n.okas_codes AS "OkasCodes",
               coalesce(s.rule_score, 0) AS "CurrentScore"
        FROM procurement_notice n
        LEFT JOIN notice_score s ON s.notice_id = n.id
        ORDER BY n.first_seen_at DESC
        LIMIT @Limit
        """;

    public async Task<IReadOnlyList<ScorableNotice>> ListForRescoreAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<ScorableNotice>(
            new CommandDefinition(ForRescore, new { Limit = limit }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    private async Task<IReadOnlyList<CommandNotice>> QueryAsync(
        string sql,
        object parameters,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<CommandNotice>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }
}
