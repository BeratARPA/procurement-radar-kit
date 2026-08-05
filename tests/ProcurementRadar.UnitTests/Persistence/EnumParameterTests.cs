using System.Data;
using Microsoft.Extensions.Time.Testing;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Persistence;
using ProcurementRadar.UnitTests.Archive;

namespace ProcurementRadar.UnitTests.Persistence;

/// <summary>
/// Every enum column in the schema is text. Dapper resolves an enum to its
/// underlying integer before it consults a type handler, so a repository that
/// hands over AuthorityType.Municipality writes the string "1" into a text
/// column and every value in the database is wrong while nothing throws.
/// </summary>
/// <remarks>
/// This happened. It survived the SQL tests, which only read the statement text,
/// and the pipeline tests, which use an in-memory repository, and only turned up
/// in a run against a real database. These tests watch the one place that can
/// see it without one: what Dapper actually put in the parameters.
/// </remarks>
public sealed class EnumParameterTests
{
    static EnumParameterTests() => PersistenceSetup.RegisterTypeHandlers();

    [Fact]
    public async Task An_authority_type_is_sent_as_its_schema_text()
    {
        var connection = new RecordingDbConnection();
        var repository = new SourceRepository(new StubConnectionFactory(connection), TimeProvider.System);

        await repository.UpsertAsync(TestSources.Municipality(), CancellationToken.None);

        var value = connection.Find("AuthorityType");

        Assert.Equal("municipality", value);
        Assert.IsType<string>(value);
    }

    [Fact]
    public async Task A_notice_status_is_sent_as_its_schema_text()
    {
        var connection = new RecordingDbConnection();
        var repository = new NoticeRepository(new StubConnectionFactory(connection), TimeProvider.System);

        await repository.UpdateStatusAsync(1, NoticeStatus.NotRelevant, CancellationToken.None);

        var value = connection.Find("Status");

        Assert.Equal("not_relevant", value);
        Assert.IsType<string>(value);
    }

    [Fact]
    public async Task A_status_filter_is_sent_as_its_schema_text()
    {
        var connection = new RecordingDbConnection(_ => NoticeTable());
        var repository = new NoticeRepository(new StubConnectionFactory(connection), TimeProvider.System);

        await repository.ListByStatusAsync(NoticeStatus.BidSubmitted, CancellationToken.None);

        Assert.Equal("bid_submitted", connection.Find("Status"));
    }

    [Fact]
    public async Task An_upserted_notice_carries_its_status_as_text()
    {
        var connection = new RecordingDbConnection(_ => UpsertResultTable());
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 9, 14, 5, 0, TimeSpan.Zero));
        var repository = new NoticeRepository(new StubConnectionFactory(connection), time);

        await repository.UpsertAsync(
            new ProcurementNotice
            {
                SourceId = 1,
                RawFetchId = 1,
                ExternalKey = "abc",
                AuthorityName = "Çankaya Belediyesi",
                Title = "Yazılım Bakım",
                SourceUrl = "https://example.gov.tr/ilan/1",
                Status = NoticeStatus.New
            },
            CancellationToken.None);

        Assert.Equal("new", connection.Find("Status"));

        // The timestamps come from the injected clock, not from the caller.
        Assert.Equal(time.GetUtcNow(), connection.Find("SeenAt"));
    }

    [Fact]
    public async Task Every_enum_member_survives_the_trip_to_a_parameter()
    {
        // A member added later has to be caught here rather than by whichever
        // notification silently stops matching.
        foreach (var status in Enum.GetValues<NoticeStatus>())
        {
            var connection = new RecordingDbConnection();
            var repository = new NoticeRepository(new StubConnectionFactory(connection), TimeProvider.System);

            await repository.UpdateStatusAsync(1, status, CancellationToken.None);

            Assert.Equal(EnumText.ToText(status), connection.Find("Status"));
        }
    }

    private static DataTable NoticeTable()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(long));
        table.Columns.Add("SourceId", typeof(long));
        table.Columns.Add("RawFetchId", typeof(long));
        table.Columns.Add("ExternalKey", typeof(string));
        table.Columns.Add("AuthorityName", typeof(string));
        table.Columns.Add("Title", typeof(string));
        table.Columns.Add("SourceUrl", typeof(string));
        table.Columns.Add("Status", typeof(string));

        return table;
    }

    private static DataTable UpsertResultTable()
    {
        var table = new DataTable();
        table.Columns.Add("NoticeId", typeof(long));
        table.Columns.Add("WasInserted", typeof(bool));
        table.Rows.Add(1L, true);

        return table;
    }
}
