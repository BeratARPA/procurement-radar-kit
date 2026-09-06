using ProcurementRadar.Domain.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Health;
using ProcurementRadar.Notifier;
using ProcurementRadar.UnitTests.Archive;
using ProcurementRadar.UnitTests.Notifications;

namespace ProcurementRadar.UnitTests.Health;

public sealed class HealthMonitorTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly FakeHealthRepository _health;
    private readonly FakeSourceRepository _sources = new();
    private readonly RecordingAlertChannel _alerts = new();

    public HealthMonitorTests()
    {
        // The fake shares the fake clock, so advancing time really does move the
        // cooldown the way the real "raised_at > now - cooldown" clause does.
        _health = new FakeHealthRepository(_time);
    }

    [Fact]
    public async Task Pages_arriving_and_nothing_reading_them_is_an_alarm()
    {
        // The one that was missing. A stopped parser looks healthy from every
        // other angle: fetches succeed, the archive grows, sources stay green,
        // and not one notice is ever created. It happened - a missing parameter
        // killed every parse run for two hours and the only thing that said so
        // was a line in a log file.
        _health.NoticesSinceWindow = 1;
        _health.UnparsedBacklog = 141;

        await Monitor().RunAsync(CancellationToken.None);

        // Asserted as "an alarm went out" rather than by its wording: the
        // wording is a setting and changing it must not fail a test about
        // whether the fault is noticed.
        Assert.NotEmpty(_alerts.Messages);
    }

    [Fact]
    public async Task An_archive_that_is_being_read_raises_nothing()
    {
        // Rows waiting a minute or two are a queue, not a stop; the window is
        // what tells them apart, and the repository applies it.
        _health.NoticesSinceWindow = 1;
        _health.UnparsedBacklog = 0;

        await Monitor().RunAsync(CancellationToken.None);

        Assert.Empty(_alerts.Messages);
    }

    [Fact]
    public async Task A_source_that_has_never_produced_anything_is_an_alarm()
    {
        // The gap the other two leave. Selectors written against a page that
        // happened to be empty that day fetch perfectly for ever and find
        // nothing, and neither ParserBroken nor SilentSource fires - one wants a
        // previous success, the other wants a month of history to have stopped.
        _health.NoticesSinceWindow = 1;
        _health.NeverProduced.Add(new NeverProducedSource
        {
            SourceId = 43,
            AuthorityName = "Mamak Belediyesi"
        });

        await Monitor().RunAsync(CancellationToken.None);

        Assert.NotEmpty(_alerts.Messages);
    }

    private HealthMonitor Monitor() => new(
        _health,
        _sources,
        _alerts,
        new Fixed<MessageTemplates>(NoticeMessageFormatterTests.Templates),
        new HealthOptions(),
        _time,
        NullLogger<HealthMonitor>.Instance);

    [Fact]
    public async Task A_page_that_changed_and_parsed_to_nothing_raises_the_parser_alarm()
    {
        // The acceptance criterion, and the failure the whole section exists for:
        // HTTP 200, no exception, no notices, and without this nobody would ever
        // find out.
        _health.BrokenParsers.Add(new BrokenParser { SourceId = 12, AuthorityName = "Çankaya Belediyesi", EmptyParses = 3 });

        var summary = await Monitor().RunAsync(CancellationToken.None);

        Assert.Equal(1, summary.AlertsRaised);

        var message = Assert.Single(_alerts.Messages);
        Assert.Contains("PARSER BOZUK", message, StringComparison.Ordinal);
        Assert.Contains("Çankaya Belediyesi", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_source_silent_for_a_month_raises_the_silent_alarm()
    {
        _health.SilentSources.Add(new SilentSource { SourceId = 12, AuthorityName = "Ankara Üniversitesi", LastNewNoticeAt = Now.AddDays(-45) });

        await Monitor().RunAsync(CancellationToken.None);

        var message = Assert.Single(_alerts.Messages);
        Assert.Contains("45 gün", message, StringComparison.Ordinal);
        Assert.Contains("Ankara Üniversitesi", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ten_failures_in_a_row_take_the_source_out_of_the_rotation()
    {
        _health.Failing.Add(new UnhealthySource { SourceId = 12, AuthorityName = "Çankaya Belediyesi", ConsecutiveFailures = 10 });

        await Monitor().RunAsync(CancellationToken.None);

        // The reason travels with the switch, and it has to be HealthRule: that
        // is the only value the quarantine sweep will ever revisit, so recording
        // Manual here would bury the source permanently.
        var (sourceId, reason) = Assert.Single(_sources.Deactivated);
        Assert.Equal(12, sourceId);
        Assert.Equal(DeactivationReason.HealthRule, reason);
        Assert.Contains("pasife alındı", Assert.Single(_alerts.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Three_failures_warn_without_taking_the_source_out()
    {
        _health.Failing.Add(new UnhealthySource { SourceId = 12, AuthorityName = "Çankaya Belediyesi", ConsecutiveFailures = 3 });

        await Monitor().RunAsync(CancellationToken.None);

        Assert.Empty(_sources.Deactivated);
        Assert.Contains("tarama hatası", Assert.Single(_alerts.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_collected_anywhere_for_a_day_is_a_system_alarm()
    {
        _health.NoticesSinceWindow = 0;

        await Monitor().RunAsync(CancellationToken.None);

        Assert.Contains("KRİTİK", Assert.Single(_alerts.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_quiet_weekend_is_not_a_system_alarm()
    {
        // 23 August 2026, a Sunday, and this fired at 09:12 with nothing wrong:
        // 1,488 pages fetched the day before, 707 that morning, and no new
        // notice since Friday.
        //
        // The first fix stepped the window over Saturdays and Sundays, and that
        // was wrong for a reason only more data showed. Measured across the whole
        // archive, authorities publish 79 notices on an average Saturday and 22
        // on a Sunday against 283-425 on a weekday: quieter, not silent. A window
        // that skips the weekend cannot report a collector that died on Saturday
        // morning until Monday.
        //
        // So the window knows nothing about days and is simply long enough.
        //
        // "Long enough" turned out not to be a length. 39 hours was the longest
        // silence in the archive when this was written; a later look found 48.8,
        // and the weekend of 5 September went past 60 with nothing wrong. A
        // threshold set below the quiet it has to tolerate does not tolerate it,
        // and raising it only trades a false alarm for a slower true one. What
        // separates the two states is whether the collector is working, which is
        // the second question CheckCollectionAsync now asks.
        _time.SetUtcNow(new DateTimeOffset(2026, 8, 23, 6, 12, 0, TimeSpan.Zero));

        // Friday's notices, the last before the quiet stretch.
        _health.NoticeTimes.Add(new DateTimeOffset(2026, 8, 21, 11, 0, 0, TimeSpan.Zero));

        await Monitor().RunAsync(CancellationToken.None);

        Assert.Empty(_alerts.Messages);
    }

    [Fact]
    public async Task A_weekend_that_collects_nothing_at_all_is_still_heard()
    {
        // The half the business-day window silenced. A collector that stops on
        // Saturday morning produces exactly this: nothing anywhere for two days,
        // on days that normally bring 79 and 22. Under the old rule the window
        // stepped back to Friday, found Friday's notices and said nothing until
        // Monday.
        _time.SetUtcNow(new DateTimeOffset(2026, 8, 23, 15, 0, 0, TimeSpan.Zero));

        // The last notice arrived on Friday morning, more than 48 hours ago.
        _health.NoticeTimes.Add(new DateTimeOffset(2026, 8, 21, 6, 0, 0, TimeSpan.Zero));

        await Monitor().RunAsync(CancellationToken.None);

        Assert.Contains("KRİTİK", Assert.Single(_alerts.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_weekend_longer_than_the_window_is_still_not_an_alarm_while_pages_arrive()
    {
        // Sunday 6 September 2026, and the last new notice was Friday at 18:25:
        // more than 48 hours, so the window alone would have raised this. Nothing
        // was wrong. The collector fetched 1,404 pages on the Saturday and 695 by
        // that afternoon, and the parser read 20,463 notices off them - every one
        // already known, because the authorities had published nothing.
        //
        // This is the case a window can never get right on its own: the silence
        // was real, longer than any measured before it, and completely benign.
        _time.SetUtcNow(new DateTimeOffset(2026, 9, 6, 11, 20, 0, TimeSpan.Zero));
        _health.NoticeTimes.Add(new DateTimeOffset(2026, 9, 4, 15, 25, 0, TimeSpan.Zero));
        _health.FetchesInPulse = 29;

        await Monitor().RunAsync(CancellationToken.None);

        Assert.Empty(_alerts.Messages);
    }

    [Fact]
    public async Task One_page_arriving_is_enough_to_say_the_collector_lives()
    {
        // The pulse is a yes-or-no question, not a rate. A slow hour is not a
        // dead one, and a threshold on the count would be a second number to get
        // wrong - the first one is what this whole change is about.
        _time.SetUtcNow(new DateTimeOffset(2026, 9, 6, 11, 20, 0, TimeSpan.Zero));
        _health.NoticeTimes.Add(new DateTimeOffset(2026, 9, 4, 15, 25, 0, TimeSpan.Zero));
        _health.FetchesInPulse = 1;

        await Monitor().RunAsync(CancellationToken.None);

        Assert.Empty(_alerts.Messages);
    }

    [Fact]
    public async Task The_window_is_the_same_length_whatever_day_it_is()
    {
        // No calendar arithmetic left to get wrong. Asked on a Thursday and asked
        // on a Sunday, the question is the same one.
        foreach (var day in new[] { 20, 23 })
        {
            _alerts.Messages.Clear();
            _health.NoticeTimes.Clear();
            _time.SetUtcNow(new DateTimeOffset(2026, 8, day, 9, 0, 0, TimeSpan.Zero));

            await Monitor().RunAsync(CancellationToken.None);

            var asked = _health.AskedSince!.Value;

            Assert.Equal(TimeSpan.FromHours(48), _time.GetUtcNow() - asked);
        }
    }

    [Fact]
    public async Task A_system_that_is_collecting_says_nothing()
    {
        _health.NoticesSinceWindow = 4;

        var summary = await Monitor().RunAsync(CancellationToken.None);

        Assert.Equal(0, summary.AlertsRaised);
        Assert.Empty(_alerts.Messages);
    }

    [Fact]
    public async Task The_same_alarm_is_not_repeated_while_it_is_still_cooling_down()
    {
        // An alarm every minute gets the channel muted, and a muted channel costs
        // the alarms that matter.
        _health.BrokenParsers.Add(new BrokenParser { SourceId = 12, AuthorityName = "Çankaya Belediyesi", EmptyParses = 3 });

        await Monitor().RunAsync(CancellationToken.None);
        await Monitor().RunAsync(CancellationToken.None);
        await Monitor().RunAsync(CancellationToken.None);

        Assert.Single(_alerts.Messages);
    }

    [Fact]
    public async Task The_alarm_comes_back_once_the_cooldown_has_passed()
    {
        _health.BrokenParsers.Add(new BrokenParser { SourceId = 12, AuthorityName = "Çankaya Belediyesi", EmptyParses = 3 });

        await Monitor().RunAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromHours(25));
        await Monitor().RunAsync(CancellationToken.None);

        Assert.Equal(2, _alerts.Messages.Count);
    }

    [Fact]
    public async Task Two_sources_with_the_same_problem_are_two_alarms()
    {
        _health.BrokenParsers.Add(new BrokenParser { SourceId = 12, AuthorityName = "Çankaya Belediyesi", EmptyParses = 3 });
        _health.BrokenParsers.Add(new BrokenParser { SourceId = 13, AuthorityName = "Keçiören Belediyesi", EmptyParses = 2 });

        await Monitor().RunAsync(CancellationToken.None);

        Assert.Equal(2, _alerts.Messages.Count);
    }

    [Fact]
    public async Task The_weekly_summary_carries_the_figures_spec_bölüm_11_asks_for()
    {
        _health.Summary = new WeeklySummary
        {
            ActiveSources = 62,
            SourcesPolled = 60,
            NoticesSeen = 14,
            NoticesNotified = 9,
            BidsSubmitted = 3,
            NotRelevant = 5,
            SourcesInTrouble = 2,
            AwaitingDecision = 48,
            OldestAwaitingDays = 7
        };

        await Monitor().SendWeeklySummaryAsync(CancellationToken.None);

        var message = Assert.Single(_alerts.Messages);

        Assert.Contains("Aktif kaynak: 62", message, StringComparison.Ordinal);
        Assert.Contains("Yeni ilan: 14", message, StringComparison.Ordinal);
        Assert.Contains("Gönderilen bildirim: 9", message, StringComparison.Ordinal);
        Assert.Contains("Teklif verilen: 3", message, StringComparison.Ordinal);
        Assert.Contains("Sorunlu kaynak: 2", message, StringComparison.Ordinal);

        // The line the report gained on 14 August 2026, and the reason it did:
        // spec Bölüm 10 keeps the buttons so filter quality can be measured, and
        // 48 of the 69 notices ever announced had no verdict on them. The number
        // is carried with its age because the count alone is a number and the two
        // together are a question - forty-eight waiting two days is a busy week,
        // forty-eight waiting three weeks is a habit that has stopped.
        Assert.Contains("Karar bekleyen: 48", message, StringComparison.Ordinal);
        Assert.Contains("7 gündür", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_waiting_still_reports_the_line_rather_than_hiding_it()
    {
        // A zero here is the good week, and it is worth seeing. A line that
        // disappears when it is satisfied is a line nobody learns to look for.
        _health.Summary = new WeeklySummary { AwaitingDecision = 0, OldestAwaitingDays = 0 };

        await Monitor().SendWeeklySummaryAsync(CancellationToken.None);

        Assert.Contains("Karar bekleyen: 0", Assert.Single(_alerts.Messages), StringComparison.Ordinal);
    }

    private sealed class RecordingAlertChannel : IAlertChannel
    {
        public List<string> Messages { get; } = [];

        public Task SendAsync(string message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHealthRepository : IHealthRepository
    {
        private readonly List<(string Kind, long? SourceId, DateTimeOffset RaisedAt)> _raised = [];
        private readonly TimeProvider _timeProvider;

        public FakeHealthRepository(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        public List<UnhealthySource> Failing { get; } = [];

        public List<BrokenParser> BrokenParsers { get; } = [];

        public List<SilentSource> SilentSources { get; } = [];

        public int NoticesSinceWindow { get; set; } = 1;

        public WeeklySummary Summary { get; set; } = new();

        public Task<IReadOnlyList<UnhealthySource>> ListFailingAsync(
            int minimumFailures,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<UnhealthySource>>(
                Failing.Where(source => source.ConsecutiveFailures >= minimumFailures).ToList());

        public Task<IReadOnlyList<BrokenParser>> ListBrokenParsersAsync(
            DateTimeOffset since,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<BrokenParser>>(BrokenParsers);

        public Task<IReadOnlyList<SilentSource>> ListSilentAsync(
            DateTimeOffset olderThan,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<SilentSource>>(
                SilentSources.Where(source => source.LastNewNoticeAt < olderThan).ToList());

        /// <summary>The instant the monitor actually asked about.</summary>
        /// <remarks>
        /// Recorded because the window is now the thing under test. Ignoring it
        /// and answering a fixed number is what let the weekend alarm ship: the
        /// count was right in every test and the question was wrong.
        /// </remarks>
        public DateTimeOffset? AskedSince { get; private set; }

        /// <summary>
        /// When notices were actually first seen, for the tests that care which
        /// window is being asked about.
        /// </summary>
        /// <remarks>
        /// Left empty, <see cref="NoticesSinceWindow"/> answers whatever it was
        /// set to and the window is ignored - which is what the older tests
        /// want, because they are about the alarm and not about the clock.
        /// </remarks>
        public List<DateTimeOffset> NoticeTimes { get; } = [];

        /// <summary>
        /// Pages archived in the pulse window. Zero by default, which is the
        /// dead collector every existing test in this file was written against:
        /// they assert the alarm fires, and it still does when nothing is being
        /// fetched.
        /// </summary>
        public int FetchesInPulse { get; set; }

        public Task<int> CountFetchesSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
            => Task.FromResult(FetchesInPulse);

        public Task<int> CountNoticesSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
        {
            AskedSince = since;

            return Task.FromResult(NoticeTimes.Count == 0
                ? NoticesSinceWindow
                : NoticeTimes.Count(at => at >= since));
        }

        public int UnparsedBacklog { get; set; }

        public List<NeverProducedSource> NeverProduced { get; } = [];

        public Task<IReadOnlyList<NeverProducedSource>> ListNeverProducedAsync(
            DateTimeOffset fetchedSince,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<NeverProducedSource>>(NeverProduced);

        public Task<int> CountUnparsedSinceAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
            => Task.FromResult(UnparsedBacklog);

        public Task<WeeklySummary> SummariseAsync(DateTimeOffset since, CancellationToken cancellationToken)
            => Task.FromResult(Summary);

        public Task<bool> TryRaiseAsync(
            HealthAlertKind kind,
            long? sourceId,
            TimeSpan cooldown,
            string? detail,
            CancellationToken cancellationToken)
        {
            // Mirrors the "not raised inside the cooldown" clause in HealthSql.
            var name = Domain.Enums.EnumText.ToText(kind);

            var now = _timeProvider.GetUtcNow();

            if (_raised.Any(entry =>
                entry.Kind == name
                && entry.SourceId == sourceId
                && entry.RaisedAt > now - cooldown))
            {
                return Task.FromResult(false);
            }

            _raised.Add((name, sourceId, now));

            return Task.FromResult(true);
        }
    }
}
