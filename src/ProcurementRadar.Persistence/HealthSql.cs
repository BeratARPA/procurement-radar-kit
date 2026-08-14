namespace ProcurementRadar.Persistence;

/// <summary>Hand-written SQL for the health checks in spec Bölüm 11 (D-01).</summary>
internal static class HealthSql
{
    internal const string ListFailing = """
        SELECT id                   AS "SourceId",
               authority_name       AS "AuthorityName",
               consecutive_failures AS "ConsecutiveFailures"
        FROM source
        WHERE is_active = true
          AND consecutive_failures >= @MinimumFailures
        ORDER BY consecutive_failures DESC, id ASC
        """;

    // A page changed - an archive row exists only when the hash differed - and
    // the parser found nothing in it. Restricted to sources that have produced
    // notices before, because a listing that has never yielded anything is an
    // empty page or a misconfigured source, not a parser that stopped working.
    internal const string ListBrokenParsers = """
        SELECT s.id             AS "SourceId",
               s.authority_name AS "AuthorityName",
               count(*)::int    AS "EmptyParses"
        FROM raw_fetch r
        JOIN source s ON s.id = r.source_id
        WHERE r.is_processed = true
          AND r.notice_count = 0
          AND r.http_status = 200
          AND r.fetched_at >= @Since
          AND EXISTS (SELECT 1 FROM procurement_notice n WHERE n.source_id = s.id)
        GROUP BY s.id, s.authority_name
        ORDER BY count(*) DESC, s.id ASC
        """;

    // Never produced anything is a different problem, so last_new_notice_at
    // being null is not silence - it is a source that has not started.
    internal const string ListSilent = """
        SELECT id                 AS "SourceId",
               authority_name     AS "AuthorityName",
               last_new_notice_at AS "LastNewNoticeAt"
        FROM source
        WHERE is_active = true
          AND last_new_notice_at IS NOT NULL
          AND last_new_notice_at < @OlderThan
        ORDER BY last_new_notice_at ASC
        """;

    internal const string CountNoticesSince = """
        SELECT count(*)::int
        FROM procurement_notice
        WHERE first_seen_at >= @Since
        """;

    // Uses the partial index the schema already carries for unprocessed rows.
    // Fetched for long enough to have had many chances, and nothing to show for
    // it. The join is on the archive rather than on a column, because there is
    // no record of when a source was switched on - only of what it has fetched.
    // A source that has been polled for a week and produced nothing.
    //
    // The keyword searches are excluded, and that is the whole difference
    // between a signal and a flood. This alarm exists to catch selectors written
    // against a page that happened to be empty that day - eleven sources were
    // switched back on in exactly that state. An EKAP search for 'kbs
    // entegrasyon' returning nothing is not that: it is the correct answer to
    // the question asked, and it will keep being correct.
    //
    // Measured on 14 August 2026: 26 sources tripped this, 23 of them EKAP
    // keyword searches. With a daily cooldown that is 26 messages a day for
    // ever, which is how spec Bölüm 9 says Telegram gets muted.
    internal const string ListNeverProduced = """
        SELECT s.id AS "SourceId", s.authority_name AS "AuthorityName"
        FROM source s
        WHERE s.is_active
          AND s.last_new_notice_at IS NULL
          AND s.adapter_key NOT IN ('ekap_tender', 'ekap_direct_procurement')
          AND EXISTS (
              SELECT 1 FROM raw_fetch r
              WHERE r.source_id = s.id AND r.fetched_at < @FetchedSince)
        ORDER BY s.authority_name
        """;

    internal const string CountUnparsed = """
        SELECT count(*)::int
        FROM raw_fetch
        WHERE is_processed = false AND fetched_at < @OlderThan
        """;

    internal const string Summarise = """
        SELECT
            (SELECT count(*)::int FROM source WHERE is_active = true)                        AS "ActiveSources",
            (SELECT count(*)::int FROM source WHERE last_fetch_at >= @Since)                 AS "SourcesPolled",
            (SELECT count(*)::int FROM procurement_notice WHERE first_seen_at >= @Since)     AS "NoticesSeen",
            (SELECT count(*)::int FROM notification WHERE sent_at >= @Since)                 AS "NoticesNotified",
            (SELECT count(*)::int FROM procurement_notice WHERE status = 'bid_submitted')    AS "BidsSubmitted",
            (SELECT count(*)::int FROM procurement_notice WHERE status = 'not_relevant')     AS "NotRelevant",
            (SELECT count(*)::int FROM source WHERE consecutive_failures >= 3)               AS "SourcesInTrouble",
            -- Announced and still unanswered. Not "status = new" on its own:
            -- most of the archive is new and always will be, because most of it
            -- was never worth telling anybody about. The question is how many of
            -- the ones we did interrupt somebody for are still waiting.
            (SELECT count(*)::int FROM procurement_notice n
              WHERE n.status = 'new'
                AND EXISTS (SELECT 1 FROM notification t
                             WHERE t.notice_id = n.id AND t.kind = 'new_notice'))  AS "AwaitingDecision",
            -- Whole days, by subtracting two dates. extract() would give the
            -- day *component* of an interval, so anything over a month reads
            -- smaller than it is - and its argument syntax puts a word after
            -- FROM that the schema test reads as a table name.
            (SELECT coalesce(max(now()::date - t.sent_at::date), 0)
               FROM notification t
               JOIN procurement_notice n ON n.id = t.notice_id
              WHERE t.kind = 'new_notice' AND n.status = 'new')                    AS "OldestAwaitingDays"
        """;

    // Raises the alarm only if the same one has not been raised inside the
    // cooldown. One statement, so two passes cannot both decide it is time.
    internal const string TryRaise = """
        INSERT INTO health_alert (kind, source_id, raised_at, detail)
        SELECT @Kind, @SourceId, @RaisedAt, @Detail
        WHERE NOT EXISTS (
            SELECT 1 FROM health_alert
            WHERE kind = @Kind
              AND source_id IS NOT DISTINCT FROM @SourceId
              AND raised_at > @Since)
        """;
}
