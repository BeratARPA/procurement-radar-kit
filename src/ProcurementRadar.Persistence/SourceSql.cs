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
                    replace(initcap(replace(authority_type, '_', ' ')), ' ', '') AS "AuthorityType",
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
                    deactivated_at        AS "DeactivatedAt",
                    replace(initcap(replace(deactivated_reason, '_', ' ')), ' ', '') AS "DeactivatedReason",
                    quarantine_probed_at  AS "QuarantineProbedAt",
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

    // The conflict branch rewrites the definition and deliberately leaves every
    // health column alone: robots_checked_at, last_fetch_at, last_success_at,
    // last_new_notice_at and consecutive_failures survive a re-seed.
    //
    // xmax is zero only on a freshly inserted row, which is how one statement can
    // report whether this source was already registered.
    internal const string ListAll = $"""
        SELECT {SelectColumns}
        FROM source
        ORDER BY is_active DESC, authority_name
        """;

    // The counter is cleared with the switch. Leaving it at ten would take the
    // source straight back out on its next failure, and the reason it failed
    // ten times is usually the thing that was just fixed.
    internal const string Activate = """
        UPDATE source
        SET is_active            = true,
            consecutive_failures = 0,
            deactivated_at       = NULL,
            deactivated_reason   = NULL,
            quarantine_probed_at = NULL
        WHERE id = @SourceId
        """;

    // is_active is deliberately not refreshed on conflict. The seed file
    // introduces sources; whether one is in the rotation is runtime state, set
    // from the interface or by the health rules, and the file has no business
    // undoing it. Re-seeding once switched eleven sources back off minutes after
    // they had been switched on, and said nothing.
    //
    // The same rule as the settings (D-16): the file is where a row comes from,
    // the database is what runs.
    internal const string Upsert = """
        INSERT INTO source (
            authority_name, authority_type, province, district, url, adapter_key,
            adapter_config, poll_interval_minutes, is_active, notes)
        VALUES (
            @AuthorityName, @AuthorityType, @Province, @District, @Url, @AdapterKey,
            @AdapterConfig::jsonb, @PollIntervalMinutes, @IsActive, @Notes)
        ON CONFLICT (url) DO UPDATE
            SET authority_name        = EXCLUDED.authority_name,
                authority_type        = EXCLUDED.authority_type,
                province              = EXCLUDED.province,
                district              = EXCLUDED.district,
                adapter_key           = EXCLUDED.adapter_key,
                adapter_config        = EXCLUDED.adapter_config,
                poll_interval_minutes = EXCLUDED.poll_interval_minutes,
                notes                 = EXCLUDED.notes
        RETURNING (xmax = 0)
        """;

    // The same insert, saying which row it was. The seed only wants to know
    // whether it created something; a screen has to be able to open what it just
    // saved, which needs the id.
    internal const string UpsertReturningId = """
        INSERT INTO source (
            authority_name, authority_type, province, district, url, adapter_key,
            adapter_config, poll_interval_minutes, is_active, notes)
        VALUES (
            @AuthorityName, @AuthorityType, @Province, @District, @Url, @AdapterKey,
            @AdapterConfig::jsonb, @PollIntervalMinutes, @IsActive, @Notes)
        ON CONFLICT (url) DO UPDATE
            SET authority_name        = EXCLUDED.authority_name,
                authority_type        = EXCLUDED.authority_type,
                province              = EXCLUDED.province,
                district              = EXCLUDED.district,
                adapter_key           = EXCLUDED.adapter_key,
                adapter_config        = EXCLUDED.adapter_config,
                poll_interval_minutes = EXCLUDED.poll_interval_minutes,
                notes                 = EXCLUDED.notes
        RETURNING id AS "Id", (xmax = 0) AS "WasCreated"
        """;

    // Edits the definition of a row that already exists, url included - which
    // the upsert above cannot do, because the url is what it matches on and a
    // changed one would insert a second source rather than move this one.
    //
    // is_active and every health column are left alone on purpose. Editing a
    // selector must not be able to put a source back in the rotation, clear a
    // failure counter, or move last_new_notice_at - the column the silent-source
    // alarm reads.
    internal const string UpdateDefinition = """
        UPDATE source
        SET authority_name        = @AuthorityName,
            authority_type        = @AuthorityType,
            province              = @Province,
            district              = @District,
            url                   = @Url,
            adapter_key           = @AdapterKey,
            adapter_config        = @AdapterConfig::jsonb,
            poll_interval_minutes = @PollIntervalMinutes,
            notes                 = @Notes
        WHERE id = @SourceId
        RETURNING id
        """;

    // Deliberately not filtered by is_active: reprocessing the archive has to
    // work for a source that has since been switched off.
    internal const string GetById = $"""
        SELECT {SelectColumns}
        FROM source
        WHERE id = @SourceId
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

    // The reason travels with the switch. Recording one without the other is how
    // a source ends up switched off for a condition nobody can name afterwards,
    // which is the state all nineteen of these were in before 11 Aug 2026.
    internal const string Deactivate = """
        UPDATE source
        SET is_active          = false,
            deactivated_at     = @Now,
            deactivated_reason = @Reason
        WHERE id = @SourceId
        """;

    // Only the reasons that can stop being true. Least recently probed first,
    // never-probed first of all, so the queue rotates instead of asking the same
    // source every day.
    internal const string ListQuarantined = $"""
        SELECT {SelectColumns}
        FROM source
        WHERE is_active = false
          AND deactivated_reason IN ('health_rule', 'site_unreachable')
        ORDER BY quarantine_probed_at ASC NULLS FIRST, id ASC
        LIMIT @Limit
        """;

    internal const string RecordQuarantineProbe = """
        UPDATE source
        SET quarantine_probed_at = @Now
        WHERE id = @SourceId
        """;
}
