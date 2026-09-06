using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ProcurementRadar.Domain.Adapters;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Fetching;
using ProcurementRadar.Domain.Health;
using ProcurementRadar.Domain.Repositories;
using ProcurementRadar.Domain.Settings;
using ProcurementRadar.Notifier;
using ProcurementRadar.UnitTests.Archive;
using ProcurementRadar.UnitTests.Notifications;

namespace ProcurementRadar.UnitTests.Health;

/// <summary>
/// The half of the health rule that was missing.
/// </summary>
/// <remarks>
/// Deactivation used to be a one-way door. <c>ListActive</c> does not return a
/// switched-off source, so it could never be fetched, so it could never succeed,
/// so the counter that took it out could never clear. ANFA A.Ş. sat out three
/// days after its host came back, with a note that said the only job left was
/// <c>is_active = true</c>.
/// </remarks>
public sealed class QuarantineSweeperTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly FakeSourceRepository _sources = new();
    private readonly RecordingAlertChannel _alerts = new();
    private readonly StubHealthRepository _health = new();
    private readonly StubAdapter _adapter = new();

    private static Source Quarantined(
        long id,
        DeactivationReason reason,
        DateTimeOffset? probedAt = null) => new()
    {
        Id = id,
        AuthorityName = "ANFA A.Ş.",
        AuthorityType = AuthorityType.SoeSubsidiary,
        Province = "Ankara",
        Url = "https://anfa.com.tr/tum-ihaleler/",
        AdapterKey = StubAdapter.KeyName,
        IsActive = false,
        AdapterConfig = """{"row_selector":".uc_ac_box"}""",
        DeactivatedReason = reason,
        QuarantineProbedAt = probedAt
    };

    private QuarantineSweeper Sweeper() => new(
        _sources,
        new SourceAdapterRegistry([_adapter]),
        _health,
        _alerts,
        new Fixed<MessageTemplates>(NoticeMessageFormatterTests.Templates),
        NullLogger<QuarantineSweeper>.Instance);

    [Fact]
    public async Task A_source_that_answers_again_goes_back_into_the_rotation()
    {
        _sources.Sources.Add(Quarantined(27, DeactivationReason.HealthRule));
        _adapter.Notices = 50;

        var summary = await Sweeper().RunAsync(5, CancellationToken.None);

        Assert.Equal(1, summary.Recovered);
        Assert.Contains(27, _sources.Activated);

        // Activating clears the reason as well. A source still carrying one would
        // be probed again tomorrow while it is happily collecting.
        var source = Assert.Single(_sources.Sources);
        Assert.True(source.IsActive);
        Assert.Null(source.DeactivatedReason);
    }

    [Fact]
    public async Task Coming_back_is_announced()
    {
        _sources.Sources.Add(Quarantined(27, DeactivationReason.HealthRule));
        _adapter.Notices = 50;

        await Sweeper().RunAsync(5, CancellationToken.None);

        var message = Assert.Single(_alerts.Messages);
        Assert.Contains("ANFA", message, StringComparison.Ordinal);

        // The count is in the message because "it answered" and "it is collecting
        // again" are different claims, and only the second one is worth waking up
        // for.
        Assert.Contains("50", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_that_answers_but_parses_to_nothing_stays_out()
    {
        // The failure this rule exists for. A parked domain, a suspended-account
        // page and a login wall all answer 200; none of them is a source that
        // came back. Judging on the status code would have put ANFA back into the
        // rotation while it was still serving suspendedpage.cgi.
        _sources.Sources.Add(Quarantined(27, DeactivationReason.HealthRule));
        _adapter.Notices = 0;

        var summary = await Sweeper().RunAsync(5, CancellationToken.None);

        Assert.Equal(0, summary.Recovered);
        Assert.Empty(_sources.Activated);
        Assert.Empty(_alerts.Messages);
    }

    [Fact]
    public async Task A_source_that_still_does_not_answer_is_left_alone()
    {
        _sources.Sources.Add(Quarantined(15, DeactivationReason.HealthRule));
        _adapter.Outcome = FetchOutcome.Failed;

        var summary = await Sweeper().RunAsync(5, CancellationToken.None);

        Assert.Equal(1, summary.Probed);
        Assert.Equal(0, summary.Recovered);
        Assert.Empty(_sources.Activated);
    }

    [Fact]
    public async Task A_probe_that_throws_still_counts_as_a_probe()
    {
        // Beypazarı's dns stopped resolving and every attempt threw. Stamping only
        // on success would leave it at the head of the queue for ever, and it
        // would be the only source any sweep ever tried.
        _sources.Sources.Add(Quarantined(15, DeactivationReason.HealthRule));
        _sources.Sources.Add(Quarantined(99, DeactivationReason.SiteUnreachable));
        _adapter.Throw = true;

        await Sweeper().RunAsync(5, CancellationToken.None);

        Assert.Equal([15L, 99L], _sources.Probed);
    }

    [Theory]
    [InlineData(DeactivationReason.NoTenderList)]
    [InlineData(DeactivationReason.Robots)]
    [InlineData(DeactivationReason.Superseded)]
    [InlineData(DeactivationReason.Manual)]
    public async Task Reasons_that_are_facts_rather_than_conditions_are_never_probed(
        DeactivationReason reason)
    {
        // A page with no tender list does not grow one overnight, a robots rule is
        // a decision rather than a fault, and a superseded source coming back
        // would only duplicate the one that replaced it. Probing these would be
        // daily traffic that can never change an answer.
        _sources.Sources.Add(Quarantined(3, reason));
        _adapter.Notices = 50;

        var summary = await Sweeper().RunAsync(5, CancellationToken.None);

        Assert.Equal(0, summary.Probed);
        Assert.Empty(_sources.Activated);
    }

    [Fact]
    public async Task A_source_with_no_selectors_is_not_asked_at_all()
    {
        // Every adapter here needs a config, so this source parses to nothing
        // however well its server answers. Fetching it daily would be a request
        // that cannot change the outcome, and the parser would log an ERROR each
        // time for a state that is expected rather than wrong.
        _sources.Sources.Add(Quarantined(99, DeactivationReason.SiteUnreachable) with
        {
            AdapterConfig = null
        });
        _adapter.Notices = 50;

        var summary = await Sweeper().RunAsync(5, CancellationToken.None);

        Assert.Equal(0, summary.Recovered);
        Assert.False(_adapter.WasFetched);

        // Still stamped, so it rotates out of the way instead of sitting at the
        // head of the queue for ever.
        Assert.Equal([99L], _sources.Probed);
    }

    [Fact]
    public async Task The_least_recently_probed_source_goes_first()
    {
        _sources.Sources.Add(Quarantined(1, DeactivationReason.HealthRule, Now.AddDays(-1)));
        _sources.Sources.Add(Quarantined(2, DeactivationReason.HealthRule, probedAt: null));
        _sources.Sources.Add(Quarantined(3, DeactivationReason.HealthRule, Now.AddDays(-9)));
        _adapter.Notices = 0;

        await Sweeper().RunAsync(5, CancellationToken.None);

        // Never probed first, then oldest. A source added today must not wait
        // behind one that has been asked every day for a month.
        Assert.Equal([2L, 3L, 1L], _sources.Probed);
    }

    [Fact]
    public async Task The_batch_size_is_respected()
    {
        // Every probe is a real request through the same politeness gate the
        // collector uses. A sweep that asked all of them at once would spend the
        // budget of a source with a deadline tonight on one that has been gone
        // since August.
        for (var id = 1; id <= 12; id++)
        {
            _sources.Sources.Add(Quarantined(id, DeactivationReason.HealthRule));
        }

        var summary = await Sweeper().RunAsync(5, CancellationToken.None);

        Assert.Equal(5, summary.Probed);
    }

    private sealed class StubAdapter : ISourceAdapter
    {
        internal const string KeyName = "stub_table";

        public string Key => KeyName;

        public int Notices { get; set; }

        public FetchOutcome Outcome { get; set; } = FetchOutcome.Fetched;

        public bool Throw { get; set; }

        public bool WasFetched { get; private set; }

        public Task<FetchResult> FetchAsync(
            Source source,
            ConditionalRequest conditional,
            CancellationToken cancellationToken)
        {
            WasFetched = true;

            if (Throw)
            {
                throw new HttpRequestException("No such host is known.");
            }

            if (Outcome != FetchOutcome.Fetched)
            {
                return Task.FromResult(FetchResult.Failed(0));
            }

            return Task.FromResult(new FetchResult(
                FetchOutcome.Fetched,
                200,
                Encoding.UTF8.GetBytes("<html><body>ok</body></html>"),
                ETag: null,
                LastModified: null,
                ContentHash: "hash",
                ContentType: "text/html; charset=utf-8"));
        }

        public IReadOnlyList<RawNotice> Parse(string body, Source source)
            => Enumerable.Range(0, Notices)
                .Select(index => new RawNotice(
                    Title: $"İlan {index}",
                    SourceUrl: source.Url,
                    AuthorityName: source.AuthorityName))
                .ToList();
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

    /// <summary>Always lets the alarm through; the cooldown has its own tests.</summary>
    private sealed class StubHealthRepository : IHealthRepository
    {
        public Task<bool> TryRaiseAsync(
            HealthAlertKind kind,
            long? sourceId,
            TimeSpan cooldown,
            string? detail,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<IReadOnlyList<UnhealthySource>> ListFailingAsync(int threshold, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<UnhealthySource>>([]);

        public Task<IReadOnlyList<BrokenParser>> ListBrokenParsersAsync(DateTimeOffset since, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<BrokenParser>>([]);

        public Task<IReadOnlyList<SilentSource>> ListSilentAsync(DateTimeOffset before, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<SilentSource>>([]);

        public Task<int> CountNoticesSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
            => Task.FromResult(1);

        public Task<int> CountFetchesSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
            => Task.FromResult(1);

        public Task<int> CountUnparsedSinceAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<IReadOnlyList<NeverProducedSource>> ListNeverProducedAsync(DateTimeOffset fetchedSince, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<NeverProducedSource>>([]);

        public Task<WeeklySummary> SummariseAsync(DateTimeOffset since, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}

/// <summary>
/// The quarantine set is written down twice - once as a WHERE clause and once as
/// <see cref="DeactivationReasons.CanRecover"/> - and nothing at build time ties
/// them together.
/// </summary>
/// <remarks>
/// This is the shape of failure CLAUDE.md keeps a table of: the test sees one
/// thing and the worker gets another. Adding a recoverable reason to the enum and
/// forgetting the statement would leave a source that nothing ever probes, and
/// every unit test would still pass because the fake filters in C#.
/// </remarks>
public sealed class QuarantineSqlAgreesWithTheEnumTests
{
    [Fact]
    public void The_statement_lists_exactly_the_reasons_that_can_recover()
    {
        var sql = typeof(ProcurementRadar.Persistence.SourceRepository).Assembly
            .GetType("ProcurementRadar.Persistence.SourceSql")!
            .GetField("ListQuarantined", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetRawConstantValue() as string;

        Assert.NotNull(sql);

        foreach (var reason in Enum.GetValues<DeactivationReason>())
        {
            var text = $"'{EnumText.ToText(reason)}'";

            Assert.Equal(reason.CanRecover(), sql!.Contains(text, StringComparison.Ordinal));
        }
    }
}
