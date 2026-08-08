using System.Data;
using Dapper;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Health;
using ProcurementRadar.Domain.Notifications;
using ProcurementRadar.Persistence;

namespace ProcurementRadar.UnitTests.Persistence;

/// <summary>
/// Every result type is materialised by the real Dapper deserialiser from a
/// reader shaped the way Npgsql shapes one.
/// </summary>
/// <remarks>
/// This exists because of a run-time failure that nothing else could see. A
/// positional record makes Dapper match a constructor by exact parameter type,
/// and Npgsql returns a timestamptz as DateTime - so a record declaring
/// DateTimeOffset compiled, passed every test that used an in-memory fake, and
/// threw the first time the query ran. The column types below are the ones
/// Npgsql actually produces.
/// </remarks>
public sealed class MaterializationTests
{
    static MaterializationTests() => PersistenceSetup.RegisterTypeHandlers();

    [Fact]
    public async Task A_command_notice_materialises()
    {
        // /liste reads this. It was written as a positional record and threw the
        // first time somebody typed the command - and because command handling
        // runs in the same loop as the buttons, it took those down with it. The
        // exact failure this class was created for, a second time.
        var table = Table(
            ("NoticeId", typeof(long)),
            ("Title", typeof(string)),
            ("AuthorityName", typeof(string)),
            ("Score", typeof(int)),
            ("BidDeadlineAt", typeof(DateTime)));

        table.Rows.Add(1L, "Yazılım Bakım Hizmeti", "Çankaya Belediyesi", 70, DateTime.UtcNow);

        var notice = Assert.Single(await ReadAsync<CommandNotice>(table));

        Assert.Equal("Yazılım Bakım Hizmeti", notice.Title);
        Assert.Equal("Çankaya Belediyesi", notice.AuthorityName);
        Assert.Equal(70, notice.Score);
        Assert.NotNull(notice.BidDeadlineAt);
    }

    [Fact]
    public async Task A_notice_detail_materialises()
    {
        // What the detail screen and /ilan both read. Two of its columns are
        // nullable in the schema and null in most rows: no EKAP notice carries a
        // rationale until it has been scored, and 814 of the first 924 notices
        // have no IKN at all.
        var table = Table(
            ("NoticeId", typeof(long)),
            ("Title", typeof(string)),
            ("AuthorityName", typeof(string)),
            ("Ikn", typeof(string)),
            ("Score", typeof(int)),
            ("Rationale", typeof(string)),
            ("BidDeadlineAt", typeof(DateTime)),
            ("SourceUrl", typeof(string)),
            ("Status", typeof(string)),
            ("AnnouncementText", typeof(string)));

        table.Rows.Add(
            7L, "Yazılım Bakım Hizmeti", "Çankaya Belediyesi", DBNull.Value, 40,
            DBNull.Value, DateTime.UtcNow, "https://example.gov.tr/ilan/7", "new", DBNull.Value);

        var detail = Assert.Single(await ReadAsync<NoticeDetail>(table));

        Assert.Null(detail.Ikn);
        Assert.Null(detail.Rationale);
        Assert.Equal("new", detail.Status);
        Assert.Equal("https://example.gov.tr/ilan/7", detail.SourceUrl);
        Assert.Equal(TimeSpan.Zero, detail.BidDeadlineAt!.Value.Offset);
    }

    [Fact]
    public async Task The_system_status_materialises()
    {
        var table = Table(
            ("ActiveSources", typeof(int)),
            ("FailingSources", typeof(int)),
            ("Notices", typeof(int)),
            ("NoticesToday", typeof(int)),
            ("Announced", typeof(int)),
            ("LastFetchAt", typeof(DateTime)));

        table.Rows.Add(41, 1, 583, 12, 13, DateTime.UtcNow);

        var status = Assert.Single(await ReadAsync<SystemStatus>(table));

        Assert.Equal(41, status.ActiveSources);
        Assert.Equal(583, status.Notices);
        Assert.NotNull(status.LastFetchAt);
    }

    [Fact]
    public async Task A_source_materialises()
    {
        var table = Table(
            ("Id", typeof(long)),
            ("AuthorityName", typeof(string)),
            ("AuthorityType", typeof(string)),
            ("Province", typeof(string)),
            ("District", typeof(string)),
            ("Url", typeof(string)),
            ("AdapterKey", typeof(string)),
            ("AdapterConfig", typeof(string)),
            ("PollIntervalMinutes", typeof(int)),
            ("IsActive", typeof(bool)),
            ("RobotsCheckedAt", typeof(DateTime)),
            ("LastFetchAt", typeof(DateTime)),
            ("LastSuccessAt", typeof(DateTime)),
            ("LastNewNoticeAt", typeof(DateTime)),
            ("ConsecutiveFailures", typeof(int)),
            ("Notes", typeof(string)));

        table.Rows.Add(
            1L, "Çankaya Belediyesi", "Municipality", "Ankara", "Çankaya",
            "https://example.gov.tr", "generic_table", null, 15, true,
            DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, 0, null);

        var source = Assert.Single(await ReadAsync<Source>(table));

        Assert.Equal("Çankaya Belediyesi", source.AuthorityName);
        Assert.Equal(Domain.Enums.AuthorityType.Municipality, source.AuthorityType);
        Assert.Equal(TimeSpan.Zero, source.LastFetchAt!.Value.Offset);
    }

    [Fact]
    public async Task A_raw_fetch_row_materialises()
    {
        var table = Table(
            ("Id", typeof(long)),
            ("SourceId", typeof(long)),
            ("FetchedAt", typeof(DateTime)),
            ("HttpStatus", typeof(int)),
            ("ETag", typeof(string)),
            ("LastModified", typeof(string)),
            ("ContentType", typeof(string)),
            ("ContentHash", typeof(string)),
            ("NoticeCount", typeof(int)),
            ("BodyPath", typeof(string)),
            ("ParserVersion", typeof(string)),
            ("IsProcessed", typeof(bool)));

        table.Rows.Add(
            1L, 12L, DateTime.UtcNow, 200, "\"v1\"", null, "text/html",
            "hash", 3, "raw/12/2026/03/09/1.html.gz", "generic_table@1", true);

        var row = Assert.Single(await ReadAsync<RawFetch>(table));

        Assert.Equal(3, row.NoticeCount);
        Assert.Equal(TimeSpan.Zero, row.FetchedAt.Offset);
    }

    [Fact]
    public async Task A_notice_materialises_including_its_array_column()
    {
        var table = Table(
            ("Id", typeof(long)),
            ("SourceId", typeof(long)),
            ("RawFetchId", typeof(long)),
            ("ExternalKey", typeof(string)),
            ("Ikn", typeof(string)),
            ("AuthorityName", typeof(string)),
            ("Title", typeof(string)),
            ("Description", typeof(string)),
            ("ProcurementType", typeof(string)),
            ("ProcedureCode", typeof(string)),
            ("OkasCodes", typeof(string[])),
            ("EstimatedCost", typeof(decimal)),
            ("Currency", typeof(string)),
            ("BidDeadlineAt", typeof(DateTime)),
            ("OpeningAt", typeof(DateTime)),
            ("SourceUrl", typeof(string)),
            ("FirstSeenAt", typeof(DateTime)),
            ("LastSeenAt", typeof(DateTime)),
            ("Status", typeof(string)));

        table.Rows.Add(
            1L, 12L, 1L, "abc", null, "Çankaya Belediyesi", "Yazılım Bakım", null,
            "DirectProcurement", "art22d", new[] { "72000000" }, 180000m, "TRY",
            DateTime.UtcNow, DBNull.Value, "https://example.gov.tr/ilan/1",
            DateTime.UtcNow, DateTime.UtcNow, "New");

        var notice = Assert.Single(await ReadAsync<ProcurementNotice>(table));

        Assert.Equal(Domain.Enums.ProcurementType.DirectProcurement, notice.ProcurementType);
        Assert.Equal(Domain.Enums.NoticeStatus.New, notice.Status);
        Assert.Equal(["72000000"], notice.OkasCodes!);
    }

    [Fact]
    public async Task A_silent_source_materialises()
    {
        // The one that actually broke.
        var table = Table(
            ("SourceId", typeof(long)),
            ("AuthorityName", typeof(string)),
            ("LastNewNoticeAt", typeof(DateTime)));

        table.Rows.Add(12L, "Ankara Üniversitesi", DateTime.UtcNow.AddDays(-45));

        var silent = Assert.Single(await ReadAsync<SilentSource>(table));

        Assert.Equal(12, silent.SourceId);
        Assert.Equal(TimeSpan.Zero, silent.LastNewNoticeAt.Offset);
    }

    [Fact]
    public async Task The_other_health_results_materialise()
    {
        var failing = Table(
            ("SourceId", typeof(long)),
            ("AuthorityName", typeof(string)),
            ("ConsecutiveFailures", typeof(int)));
        failing.Rows.Add(12L, "Çankaya Belediyesi", 4);

        Assert.Equal(4, Assert.Single(await ReadAsync<UnhealthySource>(failing)).ConsecutiveFailures);

        var broken = Table(
            ("SourceId", typeof(long)),
            ("AuthorityName", typeof(string)),
            ("EmptyParses", typeof(int)));
        broken.Rows.Add(12L, "Çankaya Belediyesi", 2);

        Assert.Equal(2, Assert.Single(await ReadAsync<BrokenParser>(broken)).EmptyParses);
    }

    [Fact]
    public async Task A_weekly_summary_materialises()
    {
        var table = Table(
            ("ActiveSources", typeof(int)),
            ("SourcesPolled", typeof(int)),
            ("NoticesSeen", typeof(int)),
            ("NoticesNotified", typeof(int)),
            ("BidsSubmitted", typeof(int)),
            ("NotRelevant", typeof(int)),
            ("SourcesInTrouble", typeof(int)));

        table.Rows.Add(62, 60, 14, 9, 3, 5, 2);

        Assert.Equal(62, Assert.Single(await ReadAsync<WeeklySummary>(table)).ActiveSources);
    }

    [Fact]
    public async Task A_notification_candidate_materialises()
    {
        var table = Table(
            ("NoticeId", typeof(long)),
            ("Title", typeof(string)),
            ("AuthorityName", typeof(string)),
            ("Description", typeof(string)),
            ("EstimatedCost", typeof(decimal)),
            ("BidDeadlineAt", typeof(DateTime)),
            ("SourceUrl", typeof(string)),
            ("RuleScore", typeof(int)),
            ("Rationale", typeof(string)),
            ("DocumentCount", typeof(int)));

        table.Rows.Add(
            1L, "Yazılım Bakım", "Çankaya Belediyesi", null, 180000m,
            DateTime.UtcNow, "https://example.gov.tr/ilan/1", 84, "yazılım bakım", 2);

        var candidate = Assert.Single(await ReadAsync<NoticeForNotification>(table));

        Assert.Equal(84, candidate.RuleScore);
        Assert.Equal(TimeSpan.Zero, candidate.BidDeadlineAt!.Value.Offset);
    }

    [Fact]
    public async Task A_notice_document_materialises()
    {
        var table = Table(
            ("Id", typeof(long)),
            ("NoticeId", typeof(long)),
            ("Url", typeof(string)),
            ("FileName", typeof(string)),
            ("MimeType", typeof(string)),
            ("SizeBytes", typeof(long)),
            ("StoragePath", typeof(string)),
            ("DownloadedAt", typeof(DateTime)),
            ("ExtractedText", typeof(string)),
            ("FailedAttempts", typeof(int)));

        table.Rows.Add(
            1L, 7L, "https://example.gov.tr/sartname.pdf", "sartname.pdf", "application/pdf",
            4096L, "doc/7/abcdef12_sartname.pdf", DateTime.UtcNow, "metin", 0);

        var document = Assert.Single(await ReadAsync<NoticeDocument>(table));

        Assert.Equal("sartname.pdf", document.FileName);
        Assert.Equal(TimeSpan.Zero, document.DownloadedAt!.Value.Offset);
    }

    private static DataTable Table(params (string Name, Type Type)[] columns)
    {
        var table = new DataTable();

        foreach (var (name, type) in columns)
        {
            var column = table.Columns.Add(name, type);
            column.AllowDBNull = true;

            if (type == typeof(DateTime))
            {
                // Npgsql hands a timestamptz back with Kind = Utc. A DataTable
                // stores dates as unspecified-local by default, which would
                // convert to the machine's offset and make the fixture lie about
                // what the driver does.
                column.DateTimeMode = DataSetDateTime.Utc;
            }
        }

        return table;
    }

    private static async Task<IReadOnlyList<T>> ReadAsync<T>(DataTable table)
    {
        var connection = new RecordingDbConnection(_ => table);
        var repository = new SqlRunner(new StubConnectionFactory(connection));

        return await repository.QueryAsync<T>();
    }

    /// <summary>Runs one query through the real Dapper pipeline.</summary>
    private sealed class SqlRunner
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public SqlRunner(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IReadOnlyList<T>> QueryAsync<T>()
        {
            await using var connection = await _connectionFactory
                .OpenConnectionAsync(CancellationToken.None);

            var rows = await connection.QueryAsync<T>("select 1");

            return rows.AsList();
        }
    }
}
