namespace ProcurementRadar.Persistence;

/// <summary>Hand-written SQL for <c>procurement_notice</c> (D-01).</summary>
internal static class NoticeSql
{
    internal const string SelectColumns = """
        id               AS "Id",
                    source_id        AS "SourceId",
                    raw_fetch_id     AS "RawFetchId",
                    external_key     AS "ExternalKey",
                    ikn              AS "Ikn",
                    authority_name   AS "AuthorityName",
                    title            AS "Title",
                    description      AS "Description",
                    replace(initcap(replace(procurement_type, '_', ' ')), ' ', '') AS "ProcurementType",
                    procedure_code   AS "ProcedureCode",
                    okas_codes       AS "OkasCodes",
                    estimated_cost   AS "EstimatedCost",
                    currency         AS "Currency",
                    bid_deadline_at  AS "BidDeadlineAt",
                    bid_deadline_time_assumed AS "BidDeadlineTimeAssumed",
                    opening_at       AS "OpeningAt",
                    source_url       AS "SourceUrl",
                    first_seen_at    AS "FirstSeenAt",
                    last_seen_at     AS "LastSeenAt",
                    replace(initcap(replace(status, '_', ' ')), ' ', '') AS "Status"
        """;

    // The conflict branch touches last_seen_at and nothing else. first_seen_at is
    // written once, on insert, and is never recomputed.
    //
    // xmax is zero only on a freshly inserted row, which is how PostgreSQL lets
    // one statement report whether this notice is new to us.
    internal const string Upsert = """
        INSERT INTO procurement_notice (
            source_id, raw_fetch_id, external_key, ikn, authority_name, title,
            description, procurement_type, procedure_code, okas_codes,
            estimated_cost, currency, bid_deadline_at, bid_deadline_time_assumed,
            opening_at, source_url, first_seen_at, last_seen_at, status)
        VALUES (
            @SourceId, @RawFetchId, @ExternalKey, @Ikn, @AuthorityName, @Title,
            @Description, @ProcurementType, @ProcedureCode, @OkasCodes,
            @EstimatedCost, @Currency, @BidDeadlineAt, @BidDeadlineTimeAssumed,
            @OpeningAt, @SourceUrl, @SeenAt, @SeenAt, @Status)
        ON CONFLICT (external_key) DO UPDATE
            SET last_seen_at = EXCLUDED.last_seen_at
        RETURNING id AS "NoticeId", (xmax = 0) AS "WasInserted"
        """;

    // Fill-only, and every clause here is load-bearing. coalesce keeps a value
    // that is already stored, the WHERE keeps the statement from touching rows
    // that have nothing to gain, and neither first_seen_at nor last_seen_at
    // appears at all: this is not a sighting, it is a field being recovered from
    // an archive that was always kept for exactly this.
    //
    // bid_deadline_at is here for the same reason the other two are, and it is
    // the one that pays: a source gets its date_selector written weeks after it
    // started collecting, and every notice stored before that keeps a null
    // deadline forever. ODTÜ is the measured case - its selector reads 10 of 10
    // against the archived page and its twelve stored notices, all from before
    // the selector existed, have no deadline at all. Thirty-two notices across
    // the archive are in that position.
    //
    // The assumed-hour flag moves only when the deadline it describes moves.
    // Copying it onto a row that already had a deadline would relabel a real
    // hour as a guess.
    internal const string FillMissing = """
        UPDATE procurement_notice
        SET procurement_type = coalesce(procurement_type, @ProcurementType),
            procedure_code   = coalesce(procedure_code, @ProcedureCode),
            bid_deadline_at  = coalesce(bid_deadline_at, @BidDeadlineAt),
            bid_deadline_time_assumed = CASE
                WHEN bid_deadline_at IS NULL AND @BidDeadlineAt IS NOT NULL
                    THEN @BidDeadlineTimeAssumed
                ELSE bid_deadline_time_assumed
            END
        WHERE id = @NoticeId
          AND (procurement_type IS NULL
               OR procedure_code IS NULL
               OR bid_deadline_at IS NULL)
        """;

    internal const string ListByStatus = $"""
        SELECT {SelectColumns}
        FROM procurement_notice
        WHERE status = @Status
        ORDER BY bid_deadline_at ASC NULLS LAST, first_seen_at DESC
        """;

    internal const string UpdateStatus = """
        UPDATE procurement_notice
        SET status = @Status
        WHERE id = @NoticeId
        """;
}
