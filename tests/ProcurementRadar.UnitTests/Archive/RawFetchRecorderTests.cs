using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Fetching;

namespace ProcurementRadar.UnitTests.Archive;

public sealed class RawFetchRecorderTests : IDisposable
{
    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "pr-archive-" + Guid.NewGuid().ToString("N"));

    private readonly FakeRawFetchRepository _rawFetches = new();
    private readonly FakeSourceRepository _sources = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 9, 14, 5, 0, TimeSpan.Zero));

    private readonly StoragePathBuilder _paths;
    private readonly FileSystemRawArchive _archive;
    private readonly RawFetchRecorder _recorder;

    public RawFetchRecorderTests()
    {
        _paths = new StoragePathBuilder(_dataDirectory);
        _archive = new FileSystemRawArchive(_paths, NullLogger<FileSystemRawArchive>.Instance);
        _recorder = new RawFetchRecorder(
            _archive,
            _rawFetches,
            _sources,
            _time,
            NullLogger<RawFetchRecorder>.Instance);
    }

    [Fact]
    public async Task The_same_body_fetched_twice_leaves_one_archive_row()
    {
        var source = TestSources.Municipality();
        var body = Encoding.UTF8.GetBytes("<html>ilan listesi</html>");

        var first = await _recorder.RecordAsync(source, Fetched(body), CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(15));
        var second = await _recorder.RecordAsync(source, Fetched(body), CancellationToken.None);

        Assert.Equal(ArchiveOutcome.Stored, first.Outcome);
        Assert.Equal(ArchiveOutcome.Unchanged, second.Outcome);
        Assert.Single(_rawFetches.Rows);

        // The unchanged pass is still a successful visit, so the source's health
        // columns move and its failure streak stays cleared.
        Assert.Equal(2, _sources.SuccessCount);
    }

    [Fact]
    public async Task A_changed_body_produces_a_second_row()
    {
        var source = TestSources.Municipality();

        await _recorder.RecordAsync(
            source,
            Fetched(Encoding.UTF8.GetBytes("<html>eski</html>")),
            CancellationToken.None);

        _time.Advance(TimeSpan.FromMinutes(15));

        var second = await _recorder.RecordAsync(
            source,
            Fetched(Encoding.UTF8.GetBytes("<html>yeni ilan</html>")),
            CancellationToken.None);

        Assert.Equal(ArchiveOutcome.Stored, second.Outcome);
        Assert.Equal(2, _rawFetches.Rows.Count);
        Assert.NotEqual(_rawFetches.Rows[0].BodyPath, _rawFetches.Rows[1].BodyPath);
    }

    [Fact]
    public async Task The_archived_bytes_come_back_exactly_as_they_arrived()
    {
        var source = TestSources.Municipality();
        // Windows-1254 bytes: the point of D-11 is that these survive the round
        // trip without anybody deciding what they mean.
        var body = Encoding.GetEncoding("windows-1254").GetBytes("Yazılım Bakım Hizmeti Alımı");

        var record = await _recorder.RecordAsync(source, Fetched(body), CancellationToken.None);

        var readBack = await _archive.ReadAsync(record.BodyPath!, CancellationToken.None);

        Assert.Equal(body, readBack);
    }

    [Fact]
    public async Task The_row_records_the_path_the_layout_prescribes()
    {
        var source = TestSources.Municipality(id: 12);

        var record = await _recorder.RecordAsync(
            source,
            Fetched(Encoding.UTF8.GetBytes("<html/>")),
            CancellationToken.None);

        Assert.Equal("raw/12/2026/03/09/1.html.gz", record.BodyPath);
        Assert.True(File.Exists(_paths.Absolute(record.BodyPath!)));
    }

    [Fact]
    public async Task The_file_is_on_disk_before_the_row_names_it()
    {
        var source = TestSources.Municipality();

        await _recorder.RecordAsync(source, Fetched(Encoding.UTF8.GetBytes("<html/>")), CancellationToken.None);

        // Nothing may point at a file that is not there: raw_fetch rows are
        // never deleted, so a hole would be permanent.
        var row = Assert.Single(_rawFetches.Rows);
        Assert.True(File.Exists(_paths.Absolute(row.BodyPath)));
        Assert.Empty(Directory.GetFiles(_dataDirectory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task The_content_type_is_kept_on_the_row()
    {
        var source = TestSources.Municipality();

        await _recorder.RecordAsync(
            source,
            Fetched(Encoding.UTF8.GetBytes("<html/>"), contentType: "text/html; charset=windows-1254"),
            CancellationToken.None);

        Assert.Equal("text/html; charset=windows-1254", Assert.Single(_rawFetches.Rows).ContentType);
    }

    [Fact]
    public async Task A_304_answer_writes_nothing()
    {
        var source = TestSources.Municipality();

        var record = await _recorder.RecordAsync(
            source,
            FetchResult.NotModified("\"v1\"", null),
            CancellationToken.None);

        Assert.Equal(ArchiveOutcome.NotModified, record.Outcome);
        Assert.Empty(_rawFetches.Rows);
        Assert.Equal(1, _sources.SuccessCount);
    }

    [Fact]
    public async Task A_robots_skip_touches_neither_the_archive_nor_the_health_columns()
    {
        var source = TestSources.Municipality();

        var record = await _recorder.RecordAsync(
            source,
            FetchResult.SkippedByRobots(),
            CancellationToken.None);

        Assert.Equal(ArchiveOutcome.Skipped, record.Outcome);
        Assert.Empty(_rawFetches.Rows);
        // Neither a success nor a failure: recording either would corrupt the
        // failure streak the alarms in T-15 read.
        Assert.Equal(0, _sources.SuccessCount);
        Assert.Equal(0, _sources.FailureCount);
    }

    [Fact]
    public async Task A_failed_fetch_moves_the_failure_counter_and_writes_nothing()
    {
        var source = TestSources.Municipality();

        var record = await _recorder.RecordAsync(source, FetchResult.Failed(502), CancellationToken.None);

        Assert.Equal(ArchiveOutcome.Failed, record.Outcome);
        Assert.Empty(_rawFetches.Rows);
        Assert.Equal(1, _sources.FailureCount);
        Assert.Equal(0, _sources.SuccessCount);
    }

    [Fact]
    public async Task Gzip_actually_shrinks_a_realistic_page()
    {
        var source = TestSources.Municipality();
        var body = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(
            "<tr><td>12.03.2026</td><td>Yazılım Bakım ve Destek Hizmeti Alımı</td></tr>", 400)));

        var record = await _recorder.RecordAsync(source, Fetched(body), CancellationToken.None);

        var onDisk = new FileInfo(_paths.Absolute(record.BodyPath!)).Length;

        // D-04's storage estimate assumes roughly five to one on HTML.
        Assert.True(
            onDisk * 5 < body.Length,
            $"{body.Length} bytes compressed to {onDisk}, which is worse than the layout assumes.");
    }

    private static FetchResult Fetched(byte[] body, string? contentType = "text/html")
        => new(
            FetchOutcome.Fetched,
            200,
            body,
            ETag: "\"v1\"",
            LastModified: null,
            ContentHash: Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant(),
            ContentType: contentType);

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }
}
