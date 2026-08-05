using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ProcurementRadar.Domain.Parsing;

namespace ProcurementRadar.Parsers;

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

/// <summary>
/// Reads the date formats Turkish notice pages use and converts them from
/// Europe/Istanbul to UTC.
/// </summary>
/// <remarks>
/// The formats, month names and filler words are data
/// (config/tr-date-patterns.json); this class is only the machinery. Nothing
/// here reads the disk or the ambient clock, so it is safe to call from inside
/// a parser.
/// </remarks>
public sealed class TurkishDateParser : ITurkishDateParser
{
    // Beyond this the match is noise that happened to look like a date. A bid
    // deadline in 1998 would drive a notification for a job nobody can bid on.
    private const int EarliestPlausibleYear = 2000;
    private const int LatestPlausibleYear = 2100;

    // Turkish casing, not invariant: "İhale" lowercases to "ihale" here and to
    // "i̇hale" under the invariant rules, and the second one matches nothing in
    // the config. "MAYIS" becomes "mayıs", which the month table also lists.
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    // The config lists ":" among the filler to strip, so that a label like
    // "Son teklif verme tarihi:" loses its colon. Removing every colon would
    // turn 10:00 into 1000, the time pattern would stop matching, and the
    // deadline would silently fall back to 23:59 - fourteen hours of slack that
    // does not exist. Only colons that are not between digits are removed.
    private static readonly Regex LooseColon = new(@"(?<!\d):(?!\d)", RegexOptions.CultureInvariant);

    private static readonly Regex CollapseWhitespace = new(@"\s+", RegexOptions.CultureInvariant);

    private readonly TurkishDatePatterns _patterns;
    private readonly ILogger<TurkishDateParser> _logger;

    public TurkishDateParser(TurkishDatePatterns patterns, ILogger<TurkishDateParser> logger)
    {
        _patterns = patterns;
        _logger = logger;
    }

    public DateTimeOffset? TryParse(string? text) => TryParseDetailed(text).Value;

    public TurkishDateParseResult TryParseDetailed(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return TurkishDateParseResult.Unreadable;
        }

        var normalized = Normalize(text);

        foreach (var pattern in _patterns.Patterns)
        {
            var match = pattern.Regex.Match(normalized);
            if (!match.Success)
            {
                continue;
            }

            // The first pattern that matches decides, even if it then fails to
            // produce a value. Falling through would let a page whose time of
            // day cannot be resolved - an hour a spring-forward skipped, say -
            // match a date-only pattern instead and come back as 23:59. That
            // turns an unreadable deadline into a wrong one, and a wrong
            // deadline is what loses a bid.
            var result = Build(pattern, match, text);
            if (result.Value is null)
            {
                _logger.LogWarning(
                    "{Text} matched pattern {PatternName} but did not resolve to an instant",
                    text,
                    pattern.Name);
            }

            return result;
        }

        _logger.LogWarning("Could not read a date out of {Text}", text);

        return TurkishDateParseResult.Unreadable;
    }

    private string Normalize(string text)
    {
        var working = text.ToLower(Turkish);

        foreach (var noise in _patterns.Noise)
        {
            working = noise == ":"
                ? LooseColon.Replace(working, " ")
                : working.Replace(noise, " ", StringComparison.Ordinal);
        }

        return CollapseWhitespace.Replace(working, " ").Trim();
    }

    private TurkishDateParseResult Build(
        TurkishDatePatterns.DatePattern pattern,
        Match match,
        string original)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < pattern.Groups.Count && i + 1 < match.Groups.Count; i++)
        {
            parts[pattern.Groups[i]] = match.Groups[i + 1].Value;
        }

        if (!TryReadInt(parts, "day", out var day)
            || !TryReadInt(parts, "year", out var year))
        {
            return TurkishDateParseResult.Unreadable;
        }

        if (!TryReadMonth(parts, out var month))
        {
            _logger.LogWarning("Unknown month name in {Text}", original);
            return TurkishDateParseResult.Unreadable;
        }

        var timeWasAssumed = false;
        if (!TryReadInt(parts, "hour", out var hour) || !TryReadInt(parts, "minute", out var minute))
        {
            if (pattern.DefaultTime is not { } fallback)
            {
                return TurkishDateParseResult.Unreadable;
            }

            hour = fallback.Hour;
            minute = fallback.Minute;
            timeWasAssumed = true;

            _logger.LogWarning(
                "No time of day in {Text}; assuming {AssumedTime}. This is a guess, not data.",
                original,
                fallback);
        }

        if (year is < EarliestPlausibleYear or > LatestPlausibleYear)
        {
            _logger.LogWarning("Year {Year} in {Text} is not plausible for a deadline", year, original);
            return TurkishDateParseResult.Unreadable;
        }

        DateTime local;
        try
        {
            local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        }
        catch (ArgumentOutOfRangeException)
        {
            _logger.LogWarning("{Text} reads as a date that does not exist", original);
            return TurkishDateParseResult.Unreadable;
        }

        return TryConvertToUtc(local, original) is { } utc
            ? new TurkishDateParseResult(utc, pattern.Name, timeWasAssumed)
            : TurkishDateParseResult.Unreadable;
    }

    private DateTimeOffset? TryConvertToUtc(DateTime local, string original)
    {
        var zone = _patterns.TimeZone;

        if (zone.IsInvalidTime(local))
        {
            // The hour a spring-forward skipped. Turkey has had no such hour
            // since 2016, but an archived page from before then still can.
            _logger.LogWarning("{Text} falls in an hour that never happened in {TimeZone}", original, zone.Id);
            return null;
        }

        var offset = zone.IsAmbiguousTime(local)
            // An hour that ran twice. Take the larger offset, which is the
            // earlier of the two instants: for a deadline, guessing early is the
            // side that cannot cost a bid.
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);

        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    private bool TryReadMonth(IReadOnlyDictionary<string, string> parts, out int month)
    {
        if (TryReadInt(parts, "month", out month))
        {
            return month is >= 1 and <= 12;
        }

        return parts.TryGetValue("month_name", out var name)
            && _patterns.Months.TryGetValue(name, out month);
    }

    private static bool TryReadInt(IReadOnlyDictionary<string, string> parts, string key, out int value)
    {
        value = 0;

        return parts.TryGetValue(key, out var text)
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
