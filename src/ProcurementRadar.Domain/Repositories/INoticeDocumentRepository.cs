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
}
