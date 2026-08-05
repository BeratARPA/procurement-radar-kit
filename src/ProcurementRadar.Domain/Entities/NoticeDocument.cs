namespace ProcurementRadar.Domain.Entities;

/// <summary>A downloaded specification file. Table <c>notice_document</c>.</summary>
public sealed record NoticeDocument
{
    public long Id { get; init; }

    public required long NoticeId { get; init; }

    public required string Url { get; init; }

    public string? FileName { get; init; }

    public string? MimeType { get; init; }

    public long? SizeBytes { get; init; }

    public string? StoragePath { get; init; }

    public DateTimeOffset? DownloadedAt { get; init; }

    /// <summary>Text pulled out of the pdf, docx or archive contents (T-12).</summary>
    public string? ExtractedText { get; init; }

    /// <summary>
    /// How many times fetching this has failed. The downloader gives up after a
    /// few, so a url that will never resolve stops costing requests.
    /// </summary>
    public int FailedAttempts { get; init; }

    /// <summary>
    /// Why this file will never be fetched, or null while it is still pending.
    /// Distinct from <see cref="FailedAttempts"/>: that counts things that went
    /// wrong, this records a settled answer - most often that robots.txt puts
    /// the file server out of bounds.
    /// </summary>
    public string? UnavailableReason { get; init; }
}
