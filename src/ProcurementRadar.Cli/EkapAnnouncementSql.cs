using Dapper;
using ProcurementRadar.Persistence;

namespace ProcurementRadar.Cli;

/// <summary>
/// The two statements the announcement run needs. Hand-written, like every other
/// query in this project (D-01).
/// </summary>
internal static class EkapAnnouncementSql
{
    /// <summary>
    /// Notices worth an announcement: scored high enough to be announced, from a
    /// source that publishes an İKN, and with no text stored yet.
    /// </summary>
    private const string ListPending = """
        SELECT n.id AS "NoticeId", n.ikn AS "Ikn"
        FROM procurement_notice n
        JOIN notice_score s ON s.notice_id = n.id
        -- A tender registration number, not a direct-procurement one: the tender
        -- search cannot find 26DT... at all, so asking would fail on every run.
        WHERE n.ikn ~ '^[0-9]{4}/[0-9]+$'
          AND s.rule_score >= @MinimumScore
          AND NOT EXISTS (
              SELECT 1 FROM notice_document d
              WHERE d.notice_id = n.id AND d.extracted_text IS NOT NULL)
        ORDER BY s.rule_score DESC, n.bid_deadline_at ASC NULLS LAST
        LIMIT @Limit
        """;

    // The row was created by RecordUrlAsync a moment earlier; this fills it in.
    // downloaded_at is set because it was: a browser fetched it, and leaving it
    // null would make the document worker try again forever.
    private const string StoreText = """
        UPDATE notice_document
        SET extracted_text = @Text,
            file_name      = @FileName,
            mime_type      = 'text/plain',
            size_bytes     = length(@Text),
            downloaded_at  = now()
        WHERE notice_id = @NoticeId
          AND url = @Url
        """;

    public static async Task<IReadOnlyList<(long NoticeId, string Ikn)>> ListPendingAsync(
        IDbConnectionFactory connectionFactory,
        int minimumScore,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<(long NoticeId, string Ikn)>(
            new CommandDefinition(
                ListPending,
                new { MinimumScore = minimumScore, Limit = limit },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    public static async Task StoreTextAsync(
        IDbConnectionFactory connectionFactory,
        long noticeId,
        string ikn,
        string text,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(
                StoreText,
                new
                {
                    NoticeId = noticeId,
                    Url = $"https://ekapv2.kik.gov.tr/ekap/search#ikn={ikn}",
                    Text = text,
                    FileName = $"ilan-{ikn.Replace('/', '-')}.txt"
                },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }
}
