using ProcurementRadar.Domain.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using ProcurementRadar.Parsers;

namespace ProcurementRadar.UnitTests.Parsing;

public sealed class TurkishDateParserTests
{
    private static readonly TurkishDatePatterns Patterns = TurkishDatePatterns.Load(
        File.ReadAllText(Path.Combine(
            RepositoryRoot.Find().FullName, "config", "tr-date-patterns.json")));

    private static readonly TurkishDateParser Parser =
        new(new Fixed<TurkishDatePatterns>(Patterns), NullLogger<TurkishDateParser>.Instance);

    /// <summary>
    /// One line per pattern in config/tr-date-patterns.json, written the way the
    /// formats appear in docs/spec.md section 7. Turkey has been on UTC+03 all
    /// year round since 2016, so the expected UTC is the local time minus three
    /// hours.
    /// </summary>
    public static TheoryData<string, string, string> Samples => new()
    {
        { "dotted_date_time", "12.03.2026 10:00", "2026-03-12T07:00:00Z" },
        { "dotted_date_time", "12/03/2026 saat 10:00", "2026-03-12T07:00:00Z" },
        { "dotted_date_time", "12.03.2026 tarihinde saat 10:00'a kadar", "2026-03-12T07:00:00Z" },
        { "dotted_date_only", "12.03.2026", "2026-03-12T20:59:00Z" },
        { "long_month_time", "12 Mart 2026 Perşembe günü saat 14:30", "2026-03-12T11:30:00Z" },
        { "long_month_only", "12 Mart 2026 Perşembe", "2026-03-12T20:59:00Z" },
        { "long_month_only", "5 Mayıs 2026", "2026-05-05T20:59:00Z" },
        { "iso", "2026-03-12T10:00", "2026-03-12T07:00:00Z" },
        { "iso", "2026-03-12 10:00", "2026-03-12T07:00:00Z" }
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void A_sample_of_every_format_reads_as_the_right_instant(
        string expectedPattern,
        string text,
        string expectedUtc)
    {
        var result = Parser.TryParseDetailed(text);

        Assert.Equal(expectedPattern, result.PatternName);
        Assert.Equal(DateTimeOffset.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture), result.Value);
    }

    [Fact]
    public void Every_pattern_in_the_config_file_has_a_sample()
    {
        var configured = Patterns.Patterns.Select(pattern => pattern.Name).Order(StringComparer.Ordinal);

        var covered = Samples
            .Select(row => (string)row[0])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        // Adding a pattern to the config without a sample fails here, rather
        // than shipping a format nobody has ever seen the parser handle.
        Assert.Equal(configured, covered);
    }

    [Fact]
    public void A_label_before_the_date_does_not_eat_the_time()
    {
        // The config lists ":" as filler so a label loses its colon. Applied
        // naively it also turns 10:00 into 1000, the time pattern stops matching
        // and the deadline quietly becomes 23:59 - fourteen hours that do not
        // exist. This is the test for that.
        var result = Parser.TryParseDetailed("Son teklif verme tarihi: 12.03.2026 saat 10:00");

        Assert.Equal("dotted_date_time", result.PatternName);
        Assert.False(result.TimeWasAssumed);
        Assert.Equal(
            DateTimeOffset.Parse("2026-03-12T07:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            result.Value);
    }

    [Theory]
    [InlineData("05.05.2026 – 14:00")]
    [InlineData("05.05.2026 — 14:00")]
    public void A_dash_between_the_date_and_the_time_does_not_cost_the_time(string text)
    {
        // BELKA A.Ş. writes its İhale Tarihi ve Saati column exactly like this,
        // with an en dash. Without the dash in the filler list the time pattern
        // stops matching, the date-only pattern picks it up instead, and a
        // 14:00 deadline silently becomes 23:59 - nine hours after bidding shut.
        // The ascii hyphen is deliberately NOT filler: 05-05-2026 uses it as a
        // date separator.
        var result = Parser.TryParseDetailed(text);

        Assert.Equal("dotted_date_time", result.PatternName);
        Assert.False(result.TimeWasAssumed);
        Assert.Equal(
            DateTimeOffset.Parse("2026-05-05T11:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            result.Value);
    }

    [Fact]
    public void A_colon_stuck_to_the_word_saat_does_not_cost_the_time()
    {
        // Ankara Atatürk Sanatoryum EAH puts the tender moment inside the title:
        // "... İhalesi (02.04.2026 saat:10:00)". Stripping the word "saat" alone
        // leaves ":10:00", the colon is not between two digits so it survives,
        // and the time pattern stops matching one character short of the hour.
        var result = Parser.TryParseDetailed(
            "2026 Yılı 96 Kalem Tıbbi Sarf ve Laboratuvar Malzeme İhalesi (02.04.2026 saat:10:00)");

        Assert.False(result.TimeWasAssumed);
        Assert.Equal(
            DateTimeOffset.Parse("2026-04-02T07:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            result.Value);
    }

    [Fact]
    public void An_ascii_hyphen_is_still_a_date_separator()
    {
        Assert.Equal(
            DateTimeOffset.Parse("2026-05-05T11:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            Parser.TryParseDetailed("05-05-2026 14:00").Value);
    }

    [Fact]
    public void A_date_with_no_time_is_flagged_as_a_guess()
    {
        var result = Parser.TryParseDetailed("12.03.2026");

        Assert.True(result.TimeWasAssumed);
    }

    [Fact]
    public void A_date_with_a_time_is_not()
    {
        Assert.False(Parser.TryParseDetailed("12.03.2026 10:00").TimeWasAssumed);
    }

    [Theory]
    [InlineData("2026-03-28 12:00", "2026-03-28T09:00:00Z")]
    [InlineData("2026-03-29 12:00", "2026-03-29T09:00:00Z")]
    [InlineData("2026-03-30 12:00", "2026-03-30T09:00:00Z")]
    public void The_last_Sunday_in_March_no_longer_shifts_anything(string text, string expectedUtc)
    {
        // The ticket asks for the daylight saving boundary. There is not one any
        // more: Turkey abandoned daylight saving in 2016 and has been on UTC+03
        // ever since. The risk today is the opposite of the one the ticket had in
        // mind - code that still assumes a spring-forward would put every
        // deadline in that week an hour out. 29 March 2026 is the last Sunday in
        // March; the offset has to stay +03 across it.
        Assert.Equal(
            DateTimeOffset.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture),
            Parser.TryParse(text));
    }

    [Fact]
    public void Before_2016_the_boundary_was_real_and_is_still_honoured()
    {
        // 29 March 2015, 03:00 local: the clocks went from 03:00 to 04:00, so
        // 12:00 that day was UTC+03 while 12:00 the day before was UTC+02. An
        // archived page from then still has to read correctly.
        Assert.Equal(
            DateTimeOffset.Parse("2015-03-28T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            Parser.TryParse("28.03.2015 12:00"));

        Assert.Equal(
            DateTimeOffset.Parse("2015-03-29T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            Parser.TryParse("29.03.2015 12:00"));
    }

    [Fact]
    public void An_hour_that_never_happened_comes_back_null_instead_of_throwing()
    {
        // 03:30 on 29 March 2015 was skipped by the spring-forward.
        Assert.Null(Parser.TryParse("29.03.2015 03:30"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("belirtilmemiş")]
    [InlineData("yakında duyurulacaktır")]
    [InlineData("32.13.2026")]
    [InlineData("12.03.1998 10:00")]
    [InlineData("31.02.2026")]
    public void Anything_unreadable_comes_back_null_and_does_not_throw(string? text)
    {
        Assert.Null(Parser.TryParse(text));
    }

    [Fact]
    public void Month_names_are_read_with_or_without_their_Turkish_letters()
    {
        var withLetters = Parser.TryParse("5 Mayıs 2026");
        var without = Parser.TryParse("5 Mayis 2026");
        var shouting = Parser.TryParse("5 MAYIS 2026");

        Assert.NotNull(withLetters);
        Assert.Equal(withLetters, without);
        // "MAYIS" lowercases to "mayıs" under Turkish rules and "mayis" under the
        // invariant ones. The config lists both, and this proves the casing used
        // here lands on one of them.
        Assert.Equal(withLetters, shouting);
    }

    [Fact]
    public void The_result_is_always_utc()
    {
        var value = Parser.TryParse("12.03.2026 10:00");

        Assert.Equal(TimeSpan.Zero, value!.Value.Offset);
    }
}
