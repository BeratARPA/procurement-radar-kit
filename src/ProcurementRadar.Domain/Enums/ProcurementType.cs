namespace ProcurementRadar.Domain.Enums;

/// <summary>
/// Stored in <c>procurement_notice.procurement_type</c> as snake_case text.
/// </summary>
public enum ProcurementType
{
    DirectProcurement,
    Tender,
    Exempt
}
