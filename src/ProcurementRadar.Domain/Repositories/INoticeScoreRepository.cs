using ProcurementRadar.Domain.Entities;

namespace ProcurementRadar.Domain.Repositories;

public interface INoticeScoreRepository
{
    /// <summary>
    /// Writes or refreshes the score for a notice.
    /// </summary>
    /// <remarks>
    /// <c>llm_score</c> is deliberately left alone. It stays null in v1 (D-03),
    /// and once something does fill it, re-running the cheap rule filter after a
    /// keyword change must not wipe the expensive answer.
    /// </remarks>
    Task UpsertAsync(NoticeScore score, CancellationToken cancellationToken);
}
