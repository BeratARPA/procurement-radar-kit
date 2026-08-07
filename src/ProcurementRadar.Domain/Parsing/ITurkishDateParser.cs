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

    /// <summary>
    /// The same reading, with what had to be guessed to produce it.
    /// </summary>
    /// <remarks>
    /// A notice that says only "12.03.2026" does not say when on that day, and
    /// the hour is the difference between having time and not having it. The
    /// guess is made here and declared here, so the notification can say it was
    /// a guess instead of presenting it as the deadline.
    /// </remarks>
    TurkishDateParseResult Read(string? text);
}
