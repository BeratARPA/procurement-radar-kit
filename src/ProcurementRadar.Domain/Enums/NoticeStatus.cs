namespace ProcurementRadar.Domain.Enums;

/// <summary>
/// Stored in <c>procurement_notice.status</c> as snake_case text. Driven by our
/// own code and by the Telegram buttons, so the set is closed.
/// </summary>
public enum NoticeStatus
{
    New,
    Reviewed,
    BidSubmitted,
    Missed,
    NotRelevant
}
