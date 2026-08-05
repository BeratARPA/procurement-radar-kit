using ProcurementRadar.Domain.Entities;

namespace ProcurementRadar.Domain.Scoring;

/// <summary>
/// What a scorer produced. <paramref name="Model"/> records which one, so a
/// later change of scoring can be told apart in the data.
/// </summary>
public sealed record NoticeScoreResult(int Value, string? Rationale, string Model);

/// <summary>
/// The port the parse pipeline scores through, so the pipeline does not have to
/// know that scoring is currently a keyword list.
/// </summary>
public interface INoticeScorer
{
    NoticeScoreResult Score(ProcurementNotice notice);
}
