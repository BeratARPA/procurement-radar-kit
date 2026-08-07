namespace ProcurementRadar.Persistence;

/// <summary>
/// SQL for the <c>award</c> table, kept where RepositorySqlTests can find it:
/// that test reads every constant here and checks each table and column against
/// the migrations. Hand-written SQL (D-01) has nothing else checking it.
/// </summary>
internal static class AwardSql
{
    /// <summary>
    /// What a tender registration number looks like: four digits, a slash, a
    /// serial. 2026/1166872.
    /// </summary>
    /// <remarks>
    /// The ikn column holds two different things. A tender carries an İKN; a
    /// direct procurement carries its own number, 26DT5002097, and the two sit in
    /// the same column because both are the identifier that notice is known by.
    ///
    /// Only the first kind has a result announcement. The first live sweep asked
    /// EKAP about four direct-procurement numbers, got nothing back for any of
    /// them, and spent about seventy seconds of browser time on each - a queue
    /// four fifths full of questions with no possible answer.
    ///
    /// PostgreSQL's ~ and .NET's Regex agree on a pattern this plain, which is
    /// what lets AwardNumberTests check it against real numbers out of the
    /// database without a database.
    /// </remarks>
    public const string TenderNumberPattern = "^[0-9]{4}/[0-9]+$";

    // Newest first: a result published last week is worth more than one from a
    // tender that closed in March, and a candidate that never gets an answer
    // falls out of the queue on its own once attempts run out.
    public const string ListPending = $"""
        SELECT n.id AS "NoticeId", n.ikn AS "Ikn", n.title AS "Title"
        FROM procurement_notice n
        LEFT JOIN award a ON a.notice_id = n.id
        WHERE n.ikn ~ '{TenderNumberPattern}'
          AND n.bid_deadline_at IS NOT NULL
          AND n.bid_deadline_at < now() - make_interval(days => @RipeDays)
          AND (a.notice_id IS NULL OR (a.winner_name IS NULL AND a.attempts < @MaximumAttempts))
        ORDER BY n.bid_deadline_at DESC
        LIMIT @Limit
        """;

    // The attempt is recorded whether or not anything was found, and a field
    // already known is never overwritten with a null: a later announcement that
    // drops a row must not erase what an earlier one said.
    public const string Record = """
        INSERT INTO award (notice_id, winner_name, contract_amount, bid_count, awarded_at, checked_at, attempts)
        VALUES (@NoticeId, @WinnerName, @ContractAmount, @BidCount, @AwardedAt, @CheckedAt, 1)
        ON CONFLICT (notice_id) DO UPDATE SET
            winner_name     = coalesce(excluded.winner_name, award.winner_name),
            contract_amount = coalesce(excluded.contract_amount, award.contract_amount),
            bid_count       = coalesce(excluded.bid_count, award.bid_count),
            awarded_at      = coalesce(excluded.awarded_at, award.awarded_at),
            checked_at      = excluded.checked_at,
            attempts        = award.attempts + 1
        """;

    // The estimate comes along because the pair is the whole point: what it was
    // expected to cost, and what it went for.
    public const string ListRecent = """
        SELECT n.title AS "Title", n.authority_name AS "AuthorityName", n.estimated_cost AS "EstimatedCost",
               a.winner_name AS "WinnerName", a.contract_amount AS "ContractAmount",
               a.bid_count AS "BidCount", a.awarded_at AS "AwardedAt"
        FROM award a
        JOIN procurement_notice n ON n.id = a.notice_id
        WHERE a.winner_name IS NOT NULL
        ORDER BY coalesce(a.awarded_at, a.checked_at) DESC
        LIMIT @Limit
        """;

}
