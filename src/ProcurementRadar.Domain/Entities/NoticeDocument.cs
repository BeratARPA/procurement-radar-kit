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
}
