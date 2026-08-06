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
                    failed_attempts AS "FailedAttempts",
                    unavailable_reason AS "UnavailableReason"
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
          AND unavailable_reason IS NULL
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

    // A tender registration number, not a direct-procurement one: the tender
    // search cannot find 26DT... at all, so asking would fail on every run.
    internal const string ListMissingAnnouncements = """
        SELECT n.id AS "NoticeId", n.ikn AS "Ikn"
        FROM procurement_notice n
        JOIN notice_score s ON s.notice_id = n.id
        WHERE n.ikn ~ '^[0-9]{4}/[0-9]+$'
          AND s.rule_score >= @MinimumScore
          AND NOT EXISTS (
              SELECT 1 FROM notice_document d
              WHERE d.notice_id = n.id AND d.extracted_text IS NOT NULL)
        ORDER BY s.rule_score DESC, n.bid_deadline_at ASC NULLS LAST
        LIMIT @Limit
        """;

    // downloaded_at is set because it was fetched: leaving it null would make the
    // document worker try to download this row forever.
    internal const string StoreAnnouncement = """
        INSERT INTO notice_document
            (notice_id, url, file_name, mime_type, size_bytes, downloaded_at, extracted_text)
        VALUES
            (@NoticeId, @Url, @FileName, 'text/plain', length(@Text), @FetchedAt, @Text)
        ON CONFLICT (notice_id, url) DO UPDATE
        SET file_name      = excluded.file_name,
            mime_type      = excluded.mime_type,
            size_bytes     = excluded.size_bytes,
            downloaded_at  = excluded.downloaded_at,
            extracted_text = excluded.extracted_text
        """;

    internal const string MarkUnavailable = """
        UPDATE notice_document
        SET unavailable_reason = @Reason
        WHERE id = @DocumentId
        """;
}
