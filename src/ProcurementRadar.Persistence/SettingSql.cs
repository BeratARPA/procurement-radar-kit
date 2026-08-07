namespace ProcurementRadar.Persistence;

/// <summary>
/// SQL for <c>setting</c> and <c>setting_history</c>, kept where
/// RepositorySqlTests can check every table and column against the migrations.
/// </summary>
internal static class SettingSql
{
    public const string List = """
        SELECT key AS "Key", value::text AS "Value", updated_at AS "UpdatedAt", updated_by AS "UpdatedBy"
        FROM setting
        ORDER BY key
        """;

    public const string Get = """
        SELECT key AS "Key", value::text AS "Value", updated_at AS "UpdatedAt", updated_by AS "UpdatedBy"
        FROM setting
        WHERE key = @Key
        """;

    public const string Version = """
        SELECT max(updated_at) FROM setting
        """;

    // The old value is filed before the new one lands, in one statement, so a
    // history gap cannot open between the two.
    public const string ArchiveCurrent = """
        INSERT INTO setting_history (key, value, replaced_at, replaced_by)
        SELECT key, value, @Now, updated_by
        FROM setting
        WHERE key = @Key
        """;

    public const string Save = """
        INSERT INTO setting (key, value, updated_at, updated_by)
        VALUES (@Key, @Value::jsonb, @Now, @UpdatedBy)
        ON CONFLICT (key) DO UPDATE SET
            value      = excluded.value,
            updated_at = excluded.updated_at,
            updated_by = excluded.updated_by
        """;

    // Seeding never overwrites. A file in the repository is the value a fresh
    // install starts from, not an instruction to a system that has been running
    // for months with something else.
    public const string InsertIfMissing = """
        INSERT INTO setting (key, value, updated_at, updated_by)
        VALUES (@Key, @Value::jsonb, @Now, @UpdatedBy)
        ON CONFLICT (key) DO NOTHING
        """;

    public const string ListHistory = """
        SELECT key AS "Key", value::text AS "Value", replaced_at AS "UpdatedAt", replaced_by AS "UpdatedBy"
        FROM setting_history
        WHERE key = @Key
        ORDER BY replaced_at DESC
        LIMIT @Limit
        """;
}
