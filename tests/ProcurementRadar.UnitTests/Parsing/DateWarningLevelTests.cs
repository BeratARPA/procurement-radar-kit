using Microsoft.Extensions.Logging;
using ProcurementRadar.Domain.Settings;
using ProcurementRadar.Parsers;

namespace ProcurementRadar.UnitTests.Parsing;

/// <summary>
/// Which unreadable texts are worth a warning.
/// </summary>
/// <remarks>
/// D-07 counts warnings in the weekly report and spec.md Bölüm 7 asks for
/// unreadable date formats to be reviewed, so the level is not decoration: a
/// warning is a request for somebody's attention. Two tender titles were making
/// that request 57 times in three days between them, and neither contains a
/// date - "2026-2027 YILLARI SONUÇ KARŞILIĞI TÜBERKÜLOZ LABORATUVARI HİZMET
/// ALIMI" is naming a contract period.
///
/// The result is <see cref="TurkishDateParseResult.Unreadable"/> either way, so
/// nothing but the log tells these two cases apart. That is why the logger is
/// captured here rather than asserting on the return value.
/// </remarks>
public sealed class DateWarningLevelTests
{
    private static readonly TurkishDatePatterns Patterns = TurkishDatePatterns.Load(
        File.ReadAllText(Path.Combine(
            RepositoryRoot.Find().FullName, "config", "tr-date-patterns.json")));

    private static LogLevel? LevelFor(string text)
    {
        var logger = new CapturingLogger();
        var parser = new TurkishDateParser(new Fixed<TurkishDatePatterns>(Patterns), logger);

        var result = parser.Read(text);

        Assert.Null(result.Value);

        return logger.Levels.Count == 0 ? null : logger.Levels[^1];
    }

    [Theory]
    // A contract period in a title. 38 warnings in three days, on its own.
    [InlineData("2026-2027 YILLARI SONUÇ KARŞILIĞI TÜBERKÜLOZ LABORATUVARI HİZMET ALIMI")]
    // Three years running. 19 more.
    [InlineData("2024-2025-2026 YILLARI TIBBİ GÖRÜNTÜLEME (MR) HİZMET ALIMI")]
    // A tender registration number, which is a year and a serial.
    [InlineData("2026/1166872")]
    // The ordinary case for sources that keep the deadline somewhere else.
    [InlineData("Temizlik Hizmeti Alımı")]
    public void A_title_that_never_carried_a_date_is_not_a_failed_parse(string text)
        => Assert.Equal(LogLevel.Debug, LevelFor(text));

    [Theory]
    // Day and month written out, but no such day exists. A real format problem.
    [InlineData("32.13.2026 tarihine kadar")]
    // A month is named, so somebody meant a date here even though no pattern
    // in the config can read it. Exactly what the weekly review is for.
    [InlineData("Mart ayının son haftası")]
    public void A_date_somebody_meant_and_we_could_not_read_still_asks_for_attention(string text)
        => Assert.Equal(LogLevel.Warning, LevelFor(text));

    /// <summary>
    /// Keeps only the level, which is the whole question here.
    /// </summary>
    private sealed class CapturingLogger : ILogger<TurkishDateParser>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Levels.Add(logLevel);
    }
}
