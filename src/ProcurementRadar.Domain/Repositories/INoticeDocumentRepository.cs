using ProcurementRadar.Domain.Entities;

namespace ProcurementRadar.Domain.Repositories;

public interface INoticeDocumentRepository
{
    /// <summary>
    /// Records that a notice links to a document, without fetching anything.
    /// Called from the parse pipeline, which may not touch the network. Repeats
    /// of a url already recorded for that notice are ignored.
    /// </summary>
    Task RecordUrlAsync(long noticeId, string url, CancellationToken cancellationToken);

    /// <summary>Documents that have not been downloaded and have not been given up on.</summary>
    Task<IReadOnlyList<NoticeDocument>> ListPendingAsync(
        int maximumAttempts,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Records a successful download and whatever text came out of it.</summary>
    Task MarkDownloadedAsync(NoticeDocument document, CancellationToken cancellationToken);

    /// <summary>Counts one failure against a document.</summary>
    Task RecordFailureAsync(long documentId, CancellationToken cancellationToken);

    /// <summary>
    /// Records that a document will not be fetched at all, and why. Unlike a
    /// failure this is not retried: the row is never listed as pending again.
    /// </summary>
    Task MarkUnavailableAsync(long documentId, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Notices worth fetching an announcement for: scored high enough to be
    /// announced, carrying a tender registration number, and with no text yet.
    /// </summary>
    Task<IReadOnlyList<(long NoticeId, string Ikn)>> ListMissingAnnouncementsAsync(
        int minimumScore,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Stores an announcement's text against a notice.</summary>
    Task StoreAnnouncementAsync(
        long noticeId,
        string url,
        string fileName,
        string text,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken);
}
