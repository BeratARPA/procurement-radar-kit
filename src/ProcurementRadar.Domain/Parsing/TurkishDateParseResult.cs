namespace ProcurementRadar.Domain.Parsing;

/// <summary>What a single attempt produced. The pattern name is reported so tests
/// can prove every pattern in the config file is exercised.</summary>
public sealed record TurkishDateParseResult(
    DateTimeOffset? Value,
    string? PatternName = null,
    bool TimeWasAssumed = false)
{
    // Cast needed: a record also has a copy constructor, and a bare null would
    // bind to that one.
    public static TurkishDateParseResult Unreadable { get; } = new((DateTimeOffset?)null);
}
