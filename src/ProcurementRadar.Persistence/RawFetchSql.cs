namespace ProcurementRadar.Persistence;

/// <summary>Hand-written SQL for <c>raw_fetch</c> (D-01). Rows are never deleted.</summary>
internal static class RawFetchSql
{
    internal const string SelectColumns = """
        id             AS "Id",
                    source_id      AS "SourceId",
                    fetched_at     AS "FetchedAt",
                    http_status    AS "HttpStatus",
                    etag           AS "ETag",
                    last_modified  AS "LastModified",
                    content_type   AS "ContentType",
                    content_hash   AS "ContentHash",
                    body_path      AS "BodyPath",
                    parser_version AS "ParserVersion",
                    is_processed   AS "IsProcessed"
        """;

    // The sequence is looked up rather than named, so renaming the table cannot
    // leave this pointing at something that no longer exists.
    internal const string ReserveId = """
        SELECT nextval(pg_get_serial_sequence('raw_fetch', 'id'))
        """;

    // The id is supplied rather than generated: the archive file is written
    // first, and its path contains the id.
    internal const string Insert = """
        INSERT INTO raw_fetch (
            id, source_id, fetched_at, http_status, etag, last_modified,
            content_type, content_hash, body_path, parser_version, is_processed)
        VALUES (
            @Id, @SourceId, @FetchedAt, @HttpStatus, @ETag, @LastModified,
            @ContentType, @ContentHash, @BodyPath, @ParserVersion, @IsProcessed)
        """;

    internal const string GetLatestContentHash = """
        SELECT content_hash
        FROM raw_fetch
        WHERE source_id = @SourceId
        ORDER BY fetched_at DESC, id DESC
        LIMIT 1
        """;

    // A null @SourceId means "every source". The cast keeps PostgreSQL from
    // having to guess the type of a null parameter.
    internal const string ListUnprocessed = $"""
        SELECT {SelectColumns}
        FROM raw_fetch
        WHERE is_processed = false
          AND (@SourceId::bigint IS NULL OR source_id = @SourceId::bigint)
        ORDER BY fetched_at ASC, id ASC
        LIMIT @Limit
        """;

    internal const string MarkProcessed = """
        UPDATE raw_fetch
        SET is_processed   = true,
            parser_version = @ParserVersion
        WHERE id = @RawFetchId
        """;
}
