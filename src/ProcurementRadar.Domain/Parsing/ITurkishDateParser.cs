namespace ProcurementRadar.Domain.Parsing;

/// <summary>
/// Reads the date formats Turkish notice pages use and converts them from
/// Europe/Istanbul to UTC (D-08).
/// </summary>
/// <remarks>
/// The patterns are data and live in config/tr-date-patterns.json, so an
/// implementation loads them when it is built, never while parsing: a parser may
/// not read from disk. Anything it cannot read comes back null and is logged at
/// WARN rather than throwing. The implementation arrives with T-08.
/// </remarks>
public interface ITurkishDateParser
{
    DateTimeOffset? TryParse(string? text);
}
