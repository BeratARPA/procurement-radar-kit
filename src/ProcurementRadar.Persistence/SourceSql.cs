namespace ProcurementRadar.Persistence;

/// <summary>
/// Hand-written SQL for <c>source</c> (D-01). Every selected column is aliased to
/// the property it fills, so the mapping is visible in the query instead of
/// depending on a Dapper naming convention.
/// </summary>
internal static class SourceSql
{
    internal const string SelectColumns = """
        id                    AS "Id",
                    authority_name        AS "AuthorityName",
                    authority_type        AS "AuthorityType",
                    province              AS "Province",
                    district              AS "District",
                    url                   AS "Url",
                    adapter_key           AS "AdapterKey",
                    adapter_config        AS "AdapterConfig",
                    poll_interval_minutes AS "PollIntervalMinutes",
                    is_active             AS "IsActive",
                    robots_checked_at     AS "RobotsCheckedAt",
                    last_fetch_at         AS "LastFetchAt",
                    last_success_at       AS "LastSuccessAt",
                    last_new_notice_at    AS "LastNewNoticeAt",
                    consecutive_failures  AS "ConsecutiveFailures",
                    notes                 AS "Notes"
        """;

    // Never-fetched sources sort first so a newly seeded page is picked up on the
    // very next pass instead of waiting behind the rest of the rotation.
    internal const string ListActive = $"""
        SELECT {SelectColumns}
        FROM source
        WHERE is_active = true
        ORDER BY last_fetch_at ASC NULLS FIRST, id ASC
        """;

    internal const string RecordFetchSuccess = """
        UPDATE source
        SET last_fetch_at        = @Now,
            last_success_at      = @Now,
            consecutive_failures = 0,
            last_new_notice_at   = CASE WHEN @FoundNewNotice THEN @Now ELSE last_new_notice_at END
        WHERE id = @SourceId
        """;

    internal const string RecordFetchFailure = """
        UPDATE source
        SET last_fetch_at        = @Now,
            consecutive_failures = consecutive_failures + 1
        WHERE id = @SourceId
        """;

    internal const string RecordRobotsCheck = """
        UPDATE source
        SET robots_checked_at = @Now
        WHERE id = @SourceId
        """;

    internal const string Deactivate = """
        UPDATE source
        SET is_active = false
        WHERE id = @SourceId
        """;
}
