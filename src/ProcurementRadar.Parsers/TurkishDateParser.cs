using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ProcurementRadar.Domain.Parsing;

namespace ProcurementRadar.Parsers;


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

    /// <summary>
    /// Whether the text has anything date-shaped in it at all.
    /// </summary>
    /// <remarks>
    /// Deliberately looser than any pattern in the config. Its only job is to
    /// separate "this was meant to be a date and I could not read it" - which is
    /// worth a warning and the weekly look spec.md Bölüm 7 asks for - from
    /// "there is no date here", which is what most rows of some listings are.
    ///
    /// A day beside a month is the shape, so at least one of the two numbers
    /// has to be short. The lookarounds are what make that true and they are
    /// the whole point: without them "2026-2027" matches on the "26-20" inside
    /// it. A bare year used to be enough on its own, which is why "2026-2027
    /// YILLARI SONUÇ KARŞILIĞI TÜBERKÜLOZ LABORATUVARI HİZMET ALIMI" warned 38
    /// times in three days. That is a contract period in a tender title, not a
    /// date anybody failed to read.
    ///
    /// The year branch that used to sit here carried two literal backspace
    /// characters where a word boundary was meant, so it never matched.
    ///
    /// Month names are checked separately, against the config that owns them,
    /// because "12 Mart 2026" has no separator to find.
    /// </remarks>
    private static readonly Regex LooksLikeADate = new(
        @"(?<!\d)\d{1,2}\s*[./-]\s*\d{1,2}(?!\d)",
        RegexOptions.CultureInvariant);

    private readonly ProcurementRadar.Domain.Settings.ICurrent<TurkishDatePatterns> _patterns;
    private readonly ILogger<TurkishDateParser> _logger;

    public TurkishDateParser(ProcurementRadar.Domain.Settings.ICurrent<TurkishDatePatterns> patterns, ILogger<TurkishDateParser> logger)
    {
        _patterns = patterns;
        _logger = logger;
    }

    public DateTimeOffset? TryParse(string? text) => TryParseDetailed(text).Value;

    public TurkishDateParseResult Read(string? text) => TryParseDetailed(text);

    public TurkishDateParseResult TryParseDetailed(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return TurkishDateParseResult.Unreadable;
        }

        var normalized = Normalize(text);

        foreach (var pattern in _patterns.Value.Patterns)
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

        // Two different things used to share this line, and only one of them is
        // worth a warning.
        //
        // "12.03.2026 tarihine kadar" that no pattern matches is a format we
        // should learn - spec.md Bölüm 7 asks for those to be reviewed weekly.
        // A tender title with no date in it is not a failed parse at all: some
        // sources put the deadline inside the link text and most of their rows
        // simply do not carry one, so the same cell is read every time and the
        // warning fires for the ordinary case. That was 211 of them in three
        // days, against a handful of real ones.
        if (LooksLikeADate.IsMatch(normalized) || NamesAMonth(normalized))
        {
            _logger.LogWarning("Could not read a date out of {Text}", text);
        }
        else
        {
            _logger.LogDebug("No date in {Text}", text);
        }

        return TurkishDateParseResult.Unreadable;
    }

    /// <summary>
    /// Whether a month the config knows is written out in the text.
    /// </summary>
    /// <remarks>
    /// Asked of the normalized text, which is already lower-cased the Turkish
    /// way, so the keys match as the file writes them.
    /// </remarks>
    private bool NamesAMonth(string normalized)
    {
        foreach (var month in _patterns.Value.Months.Keys)
        {
            if (normalized.Contains(month, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private string Normalize(string text)
    {
        var working = text.ToLower(Turkish);

        foreach (var noise in _patterns.Value.Noise)
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

            // Debug, not warning, and the distinction is between "something went
            // wrong" and "something is worth knowing about this notice".
            //
            // The guess is real and it matters - the assumed hour can be several
            // hours off the actual one, on the field spec Bölüm 7 calls the most
            // important. But it does not travel in this log line: the notice
            // carries `bid_deadline_time_assumed` all the way to the Telegram
            // message, where the reader sees it beside that specific deadline.
            //
            // Left at warning it fired 4,112 times a day - two thirds of every
            // notice, re-counted on every reparse - and D-07 counts warnings in
            // the weekly report. A warning that fires for the expected case is a
            // warning nobody can read the others through.
            _logger.LogDebug(
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
        var zone = _patterns.Value.TimeZone;

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
            && _patterns.Value.Months.TryGetValue(name, out month);
    }

    private static bool TryReadInt(IReadOnlyDictionary<string, string> parts, string key, out int value)
    {
        value = 0;

        return parts.TryGetValue(key, out var text)
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
