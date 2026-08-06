namespace ProcurementRadar.Persistence;

/// <summary>Hand-written SQL for <c>notification</c> (D-01).</summary>
internal static class NotificationSql
{
    private const string Columns = """
        n.id             AS "NoticeId",
                    n.title          AS "Title",
                    n.authority_name AS "AuthorityName",
                    n.description    AS "Description",
                    n.estimated_cost AS "EstimatedCost",
                    n.bid_deadline_at AS "BidDeadlineAt",
                    n.source_url     AS "SourceUrl",
                    s.rule_score     AS "RuleScore",
                    s.rationale      AS "Rationale",
                    (SELECT count(*) FROM notice_document d
                      WHERE d.notice_id = n.id AND d.downloaded_at IS NOT NULL) AS "DocumentCount",
                    n.ikn            AS "Ikn",
                    -- The longest extracted text wins: a notice often carries a
                    -- one-page teklif cetveli beside a real technical spec, and
                    -- the spec is the one that says what is being bought.
                    (SELECT d.extracted_text FROM notice_document d
                      WHERE d.notice_id = n.id AND d.extracted_text IS NOT NULL
                      ORDER BY length(d.extracted_text) DESC
                      LIMIT 1) AS "DocumentExcerpt"
        """;

    // Nearest deadline first: if the batch is capped, the ones running out of
    // time are the ones that get sent.
    internal const string ListUnannounced = $"""
        SELECT {Columns}
        FROM procurement_notice n
        JOIN notice_score s ON s.notice_id = n.id
        WHERE n.status = 'new'
          AND s.rule_score >= @MinimumScore
          AND NOT EXISTS (
              SELECT 1 FROM notification x
              WHERE x.notice_id = n.id AND x.kind = 'new_notice')
        ORDER BY n.bid_deadline_at ASC NULLS LAST, n.first_seen_at ASC
        LIMIT @Limit
        """;

    // Still 'new' is the point: somebody who already pressed a button does not
    // need chasing, and a notice already past its deadline cannot be bid on.
    internal const string ListDueReminders = $"""
        SELECT {Columns}
        FROM procurement_notice n
        JOIN notice_score s ON s.notice_id = n.id
        WHERE n.status = 'new'
          AND s.rule_score >= @MinimumScore
          AND n.bid_deadline_at IS NOT NULL
          AND n.bid_deadline_at > @Now
          AND n.bid_deadline_at <= @Deadline
          AND EXISTS (
              SELECT 1 FROM notification x
              WHERE x.notice_id = n.id AND x.kind = 'new_notice')
          AND NOT EXISTS (
              SELECT 1 FROM notification x
              WHERE x.notice_id = n.id AND x.kind = 'deadline_reminder')
        ORDER BY n.bid_deadline_at ASC
        LIMIT @Limit
        """;

    // The unique constraint from 005 is what makes this safe: two passes racing
    // cannot both insert, and the loser is told so instead of sending twice.
    internal const string TryRecord = """
        INSERT INTO notification (notice_id, channel, kind, sent_at, status)
        VALUES (@NoticeId, @Channel, @Kind, @SentAt, @Status)
        ON CONFLICT (notice_id, channel, kind) DO NOTHING
        """;

    internal const string Release = """
        DELETE FROM notification
        WHERE notice_id = @NoticeId AND channel = @Channel AND kind = @Kind
        """;

    internal const string MarkSent = """
        UPDATE notification
        SET status = @Status, sent_at = @SentAt
        WHERE notice_id = @NoticeId AND channel = @Channel AND kind = @Kind
        """;
}
