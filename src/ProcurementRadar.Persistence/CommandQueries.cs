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

    // Still new, still scored, and not already closed. A notice whose deadline has
    // passed is not open however high it scored, and showing it would waste the
    // one screen somebody is looking at.
    private const string ListOpen = """
        SELECT n.id AS "NoticeId", n.title AS "Title", s.rule_score AS "Score",
               n.bid_deadline_at AS "BidDeadlineAt"
        FROM procurement_notice n
        JOIN notice_score s ON s.notice_id = n.id
        WHERE n.status = 'new'
          AND s.rule_score >= @MinimumScore
          AND (n.bid_deadline_at IS NULL OR n.bid_deadline_at > now())
        ORDER BY n.bid_deadline_at ASC NULLS LAST, s.rule_score DESC
        LIMIT @Limit
        """;

    private const string ListSeenSince = """
        SELECT n.id AS "NoticeId", n.title AS "Title",
               coalesce(s.rule_score, 0) AS "Score", n.bid_deadline_at AS "BidDeadlineAt"
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
