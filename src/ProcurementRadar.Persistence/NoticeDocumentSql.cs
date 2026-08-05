namespace ProcurementRadar.Persistence;

/// <summary>Hand-written SQL for <c>notice_document</c> (D-01).</summary>
internal static class NoticeDocumentSql
{
    internal const string SelectColumns = """
        id             AS "Id",
                    notice_id      AS "NoticeId",
                    url            AS "Url",
                    file_name      AS "FileName",
                    mime_type      AS "MimeType",
                    size_bytes     AS "SizeBytes",
                    storage_path   AS "StoragePath",
                    downloaded_at  AS "DownloadedAt",
                    extracted_text AS "ExtractedText",
                    failed_attempts AS "FailedAttempts"
        """;

    // Reprocessing the archive parses the same page again and offers the same
    // urls. Doing nothing on conflict is what keeps that from queueing the same
    // download over and over.
    internal const string RecordUrl = """
        INSERT INTO notice_document (notice_id, url)
        VALUES (@NoticeId, @Url)
        ON CONFLICT (notice_id, url) DO NOTHING
        """;

    internal const string ListPending = $"""
        SELECT {SelectColumns}
        FROM notice_document
        WHERE downloaded_at IS NULL
          AND failed_attempts < @MaximumAttempts
        ORDER BY id ASC
        LIMIT @Limit
        """;

    internal const string MarkDownloaded = """
        UPDATE notice_document
        SET file_name      = @FileName,
            mime_type      = @MimeType,
            size_bytes     = @SizeBytes,
            storage_path   = @StoragePath,
            downloaded_at  = @DownloadedAt,
            extracted_text = @ExtractedText
        WHERE id = @Id
        """;

    internal const string RecordFailure = """
        UPDATE notice_document
        SET failed_attempts = failed_attempts + 1
        WHERE id = @DocumentId
        """;
}
