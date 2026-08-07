using ProcurementRadar.Domain.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ProcurementRadar.Domain.Entities;
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

        Assert.Contains(12, _sources.Deactivated);
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
            SourcesInTrouble = 2
        };

        await Monitor().SendWeeklySummaryAsync(CancellationToken.None);

        var message = Assert.Single(_alerts.Messages);

        Assert.Contains("Aktif kaynak: 62", message, StringComparison.Ordinal);
        Assert.Contains("Yeni ilan: 14", message, StringComparison.Ordinal);
        Assert.Contains("Gönderilen bildirim: 9", message, StringComparison.Ordinal);
        Assert.Contains("Teklif verilen: 3", message, StringComparison.Ordinal);
        Assert.Contains("Sorunlu kaynak: 2", message, StringComparison.Ordinal);
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

        public Task<int> CountNoticesSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
            => Task.FromResult(NoticesSinceWindow);

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
