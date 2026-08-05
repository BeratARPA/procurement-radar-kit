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
                    content_hash   AS "ContentHash",
                    body_path      AS "BodyPath",
                    parser_version AS "ParserVersion",
                    is_processed   AS "IsProcessed"
        """;

    internal const string Insert = """
        INSERT INTO raw_fetch (
            source_id, fetched_at, http_status, etag, last_modified,
            content_hash, body_path, parser_version, is_processed)
        VALUES (
            @SourceId, @FetchedAt, @HttpStatus, @ETag, @LastModified,
            @ContentHash, @BodyPath, @ParserVersion, @IsProcessed)
        RETURNING id
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
