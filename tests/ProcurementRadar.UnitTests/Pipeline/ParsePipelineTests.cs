using ProcurementRadar.Domain.Settings;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Adapters;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Fetching;
using ProcurementRadar.Domain.Normalization;
using ProcurementRadar.UnitTests.Archive;
using ProcurementRadar.UnitTests.Normalization;
using ProcurementRadar.Scoring;

namespace ProcurementRadar.UnitTests.Pipeline;

/// <summary>
/// The pipeline is tested with a stand-in adapter rather than real HTML. What is
/// under test here is that bytes come back out of the archive, get decoded, get
/// masked and reach the notice table; whether a given site's markup is read
/// correctly is what the fixture tests in ProcurementRadar.Parsers.Tests are for.
/// </summary>
public sealed class ParsePipelineTests : IDisposable
{
    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "pr-pipeline-" + Guid.NewGuid().ToString("N"));

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 9, 14, 5, 0, TimeSpan.Zero));
    private readonly FakeRawFetchRepository _rawFetches = new();
    private readonly FakeSourceRepository _sources;
    private readonly FakeNoticeRepository _notices;
    private readonly StoragePathBuilder _paths;
    private readonly FileSystemRawArchive _archive;
    private readonly StubAdapter _adapter = new();
    private readonly FakeNoticeScoreRepository _scores = new();
    private readonly FakeNoticeDocumentRepository _documents = new();

    // The real scorer, reading the same keywords.json the worker does.
    private static readonly RuleScorer Scorer = new(
        new Fixed<KeywordRules>(KeywordRules.Load(File.ReadAllText(Path.Combine(
            RepositoryRoot.Find().FullName, "config", "keywords.json")))),
        new NoticeNormalizer(NoticeNormalizerTests.Rules));

    private static readonly PersonalDataMasker Masker = new(PersonalDataRules.Load(
        File.ReadAllText(Path.Combine(
            RepositoryRoot.Find().FullName, "config", "tr-personal-data.json"))));

    public ParsePipelineTests()
    {
        _sources = new FakeSourceRepository(TestSources.Municipality());
        _notices = new FakeNoticeRepository(_time);
        _paths = new StoragePathBuilder(_dataDirectory);
        _archive = new FileSystemRawArchive(_paths, NullLogger<FileSystemRawArchive>.Instance);
    }

    [Fact]
    public async Task An_archived_body_becomes_a_notice()
    {
        await ArchiveAsync(Encoding.UTF8.GetBytes("<html/>"));
        _adapter.Returns(new RawNotice(
            "Yazılım Bakım ve Destek Hizmeti Alımı",
            "https://example.gov.tr/ilan/1",
            BidDeadlineAt: new DateTimeOffset(2026, 3, 14, 11, 0, 0, TimeSpan.Zero)));

        var summary = await Pipeline().RunAsync(null, 10, CancellationToken.None);

        Assert.Equal(1, summary.ArchiveRowsRead);
        Assert.Equal(1, summary.NoticesNew);

        var notice = Assert.Single(_notices.Notices);
        Assert.Equal("Yazılım Bakım ve Destek Hizmeti Alımı", notice.Title);
        Assert.Equal("Çankaya Belediyesi", notice.AuthorityName);
        Assert.Equal(12, notice.SourceId);
        Assert.NotEmpty(notice.ExternalKey);
    }

    [Fact]
    public async Task A_processed_row_is_marked_and_not_read_again()
    {
        await ArchiveAsync(Encoding.UTF8.GetBytes("<html/>"));
        _adapter.Returns(new RawNotice("Bir ilan", "https://example.gov.tr/ilan/1"));

        await Pipeline().RunAsync(null, 10, CancellationToken.None);
        var second = await Pipeline().RunAsync(null, 10, CancellationToken.None);

        Assert.Equal(0, second.ArchiveRowsRead);
        Assert.Equal("generic_table@1", Assert.Single(_rawFetches.Processed).ParserVersion);
    }

    [Fact]
    public async Task The_same_notice_seen_twice_stays_one_row_and_keeps_its_first_sighting()
    {
        var notice = new RawNotice(
            "Yazılım Bakım Hizmeti Alımı",
            "https://example.gov.tr/ilan/1",
            BidDeadlineAt: new DateTimeOffset(2026, 3, 14, 11, 0, 0, TimeSpan.Zero));

        await ArchiveAsync(Encoding.UTF8.GetBytes("<html>1</html>"));
        _adapter.Returns(notice);
        await Pipeline().RunAsync(null, 10, CancellationToken.None);

        var firstSeen = Assert.Single(_notices.Notices).FirstSeenAt;

        _time.Advance(TimeSpan.FromHours(6));
        await ArchiveAsync(Encoding.UTF8.GetBytes("<html>2</html>"));
        // Worded differently by the same authority on a later pass.
        _adapter.Returns(notice with { Title = "Yazılım Bakım" });
        var second = await Pipeline().RunAsync(null, 10, CancellationToken.None);

        Assert.Equal(0, second.NoticesNew);

        var stored = Assert.Single(_notices.Notices);
        Assert.Equal(firstSeen, stored.FirstSeenAt);
        Assert.Equal(firstSeen.AddHours(6), stored.LastSeenAt);
    }

    [Fact]
    public async Task Contact_details_never_reach_the_notice_table()
    {
        await ArchiveAsync(Encoding.UTF8.GetBytes("<html/>"));
        _adapter.Returns(new RawNotice(
            "Yazılım Bakım Hizmeti",
            "https://example.gov.tr/ilan/1",
            Description: "Mevcut sisteme modül eklenmesi. İrtibat: Ahmet Yılmaz, Tel: 0312 123 45 67, "
                + "e-posta: ahmet.yilmaz@cankaya.bel.tr"));

        await Pipeline().RunAsync(null, 10, CancellationToken.None);

        var description = Assert.Single(_notices.Notices).Description!;

        Assert.DoesNotContain("Ahmet", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Yılmaz", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0312", description, StringComparison.Ordinal);
        Assert.DoesNotContain("@cankaya.bel.tr", description, StringComparison.Ordinal);
        // The part that is actually about the work survives.
        Assert.Contains("modül eklenmesi", description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_windows_1254_body_comes_back_readable()
    {
        // D-11 in one test: the archive kept the bytes, so the encoding is worked
        // out here and the Turkish characters survive the round trip.
        var body = Encoding.GetEncoding("windows-1254").GetBytes("<html>Yazılım Bakım</html>");
        await ArchiveAsync(body, contentType: "text/html; charset=windows-1254");

        string? seenByAdapter = null;
        _adapter.Capture(text => seenByAdapter = text);
        _adapter.Returns(new RawNotice("Yazılım Bakım", "https://example.gov.tr/ilan/1"));

        await Pipeline().RunAsync(null, 10, CancellationToken.None);

        Assert.Contains("Yazılım Bakım", seenByAdapter!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_row_whose_adapter_is_missing_is_left_for_later()
    {
        await ArchiveAsync(Encoding.UTF8.GetBytes("<html/>"));

        var pipeline = new ParsePipeline(
            _archive,
            _rawFetches,
            _sources,
            _notices,
            new SourceAdapterRegistry([]),
            new ExternalKeyBuilder(new NoticeNormalizer(NoticeNormalizerTests.Rules)),
            Masker,
            _documents,
            Scorer,
            _scores,
            NullLogger<ParsePipeline>.Instance);

        var summary = await pipeline.RunAsync(null, 10, CancellationToken.None);

        Assert.Equal(1, summary.RowsSkipped);
        // Not marked processed: the row has to be waiting when the adapter lands.
        Assert.Empty(_rawFetches.Processed);
        Assert.Empty(_notices.Notices);
    }

    [Fact]
    public async Task Reprocessing_one_source_leaves_the_others_alone()
    {
        _sources.Sources.Add(TestSources.Municipality(id: 99) with { AuthorityName = "Keçiören Belediyesi" });
        await ArchiveAsync(Encoding.UTF8.GetBytes("<html>a</html>"), sourceId: 12);
        await ArchiveAsync(Encoding.UTF8.GetBytes("<html>b</html>"), sourceId: 99);
        _adapter.Returns(new RawNotice("Bir ilan", "https://example.gov.tr/ilan/1"));

        var summary = await Pipeline().RunAsync(sourceId: 99, 10, CancellationToken.None);

        Assert.Equal(1, summary.ArchiveRowsRead);
        Assert.Equal(99, Assert.Single(_notices.Notices).SourceId);
    }

    [Fact]
    public async Task A_source_that_produced_something_new_has_it_recorded()
    {
        await ArchiveAsync(Encoding.UTF8.GetBytes("<html/>"));
        _adapter.Returns(new RawNotice("Bir ilan", "https://example.gov.tr/ilan/1"));

        await Pipeline().RunAsync(null, 10, CancellationToken.None);

        // last_new_notice_at is what the silent-source alarm reads.
        Assert.Equal(1, _sources.NewNoticeCount);
    }

    private ParsePipeline Pipeline() => new(
        _archive,
        _rawFetches,
        _sources,
        _notices,
        new SourceAdapterRegistry([_adapter]),
        new ExternalKeyBuilder(new NoticeNormalizer(NoticeNormalizerTests.Rules)),
        Masker,
        _documents,
        Scorer,
        _scores,
        NullLogger<ParsePipeline>.Instance);

    private async Task ArchiveAsync(byte[] body, long sourceId = 12, string? contentType = "text/html")
    {
        var id = await _rawFetches.ReserveIdAsync(CancellationToken.None);
        var fetchedAt = _time.GetUtcNow();
        var path = await _archive.WriteAsync(sourceId, id, fetchedAt, body, CancellationToken.None);

        await _rawFetches.InsertAsync(
            new RawFetch
            {
                Id = id,
                SourceId = sourceId,
                FetchedAt = fetchedAt,
                ContentHash = Guid.NewGuid().ToString("N"),
                BodyPath = path,
                ContentType = contentType
            },
            CancellationToken.None);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private sealed class StubAdapter : ISourceAdapter
    {
        private IReadOnlyList<RawNotice> _notices = [];
        private Action<string>? _capture;

        public string Key => "generic_table";

        public void Returns(params RawNotice[] notices) => _notices = notices;

        public void Capture(Action<string> capture) => _capture = capture;

        public Task<FetchResult> FetchAsync(
            Source source,
            ConditionalRequest conditional,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("The parse pipeline must not fetch anything.");

        public IReadOnlyList<RawNotice> Parse(string body, Source source)
        {
            _capture?.Invoke(body);
            return _notices;
        }
    }
}
