namespace ProcurementRadar.Domain.Enums;

/// <summary>
/// Stored in <c>source.authority_type</c> as snake_case text. The set is closed:
/// it mirrors the source registry groups in docs/spec.md section 5.
/// </summary>
public enum AuthorityType
{
    University,
    Municipality,
    ProvincialDirectorate,
    Hospital,
    SoeSubsidiary,
    Ekap
}
