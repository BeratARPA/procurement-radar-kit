using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Fetching;
using ProcurementRadar.UnitTests.Archive;

namespace ProcurementRadar.UnitTests.Documents;

public sealed class DocumentDownloaderTests : IDisposable
{
    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "pr-docs-" + Guid.NewGuid().ToString("N"));

    private readonly FakeNoticeDocumentRepository _documents = new();
    private readonly StubFetcher _fetcher = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 9, 14, 5, 0, TimeSpan.Zero));
    private readonly StoragePathBuilder _paths;
    private readonly DocumentDownloader _downloader;

    public DocumentDownloaderTests()
    {
        _paths = new StoragePathBuilder(_dataDirectory);
        _downloader = new DocumentDownloader(
            _fetcher,
            _documents,
            _paths,
            new DocumentTextExtractor(NullLogger<DocumentTextExtractor>.Instance),
            _time,
            NullLogger<DocumentDownloader>.Instance);
    }

    [Fact]
    public async Task A_document_is_kept_and_its_text_pulled_out()
    {
        const string Url = "https://example.gov.tr/dosya/sartname.pdf";
        _fetcher.Returns(Url, SampleFiles.Pdf("Yazilim Bakim Hizmeti"), "application/pdf");
        await _documents.RecordUrlAsync(7, Url, CancellationToken.None);

        var summary = await _downloader.RunAsync(10, CancellationToken.None);

        Assert.Equal(1, summary.Downloaded);

        var document = Assert.Single(_documents.Documents);
        Assert.Equal("sartname.pdf", document.FileName);
        Assert.Equal("application/pdf", document.MimeType);
        Assert.Equal(_time.GetUtcNow(), document.DownloadedAt);
        Assert.Contains("Yazilim Bakim Hizmeti", document.ExtractedText!, StringComparison.Ordinal);
        Assert.True(File.Exists(_paths.Absolute(document.StoragePath!)));
    }

    [Fact]
    public async Task The_stored_path_follows_the_layout_in_D_04()
    {
        const string Url = "https://example.gov.tr/dosya/sartname.pdf";
        _fetcher.Returns(Url, SampleFiles.Text("içerik"));
        await _documents.RecordUrlAsync(7, Url, CancellationToken.None);

        await _downloader.RunAsync(10, CancellationToken.None);

        // doc/{notice_id}/{sha256_first8}_{sanitized_file_name}
        var path = Assert.Single(_documents.Documents).StoragePath!;

        Assert.StartsWith("doc/7/", path, StringComparison.Ordinal);
        Assert.EndsWith("_sartname.pdf", path, StringComparison.Ordinal);
        Assert.Matches(@"^doc/7/[0-9a-f]{8}_sartname\.pdf$", path);
    }

    [Fact]
    public async Task A_document_that_cannot_be_fetched_counts_a_failure_and_is_dropped_eventually()
    {
        const string Url = "https://example.gov.tr/yok.pdf";
        _fetcher.Fails(Url);
        await _documents.RecordUrlAsync(7, Url, CancellationToken.None);

        for (var attempt = 0; attempt < DocumentDownloader.MaximumAttempts; attempt++)
        {
            await _downloader.RunAsync(10, CancellationToken.None);
        }

        Assert.Equal(DocumentDownloader.MaximumAttempts, Assert.Single(_documents.Documents).FailedAttempts);

        // The url is now left alone. A file that will never resolve must stop
        // costing requests to a host we have to stay welcome at.
        var summary = await _downloader.RunAsync(10, CancellationToken.None);

        Assert.Equal(0, summary.Considered);
    }

    [Fact]
    public async Task A_document_robots_forbids_is_recorded_as_such_and_never_asked_for_again()
    {
        // Not hypothetical. Both live sources link every specification file to
        // dosyaism.saglik.gov.tr, whose robots.txt is "User-agent: * /
        // Disallow: /", so all thirty-two of them land here.
        const string Url = "https://dosyaism.example.gov.tr/Eklenti/1/0/teknik-sartname.pdf";
        _fetcher.Forbids(Url);
        await _documents.RecordUrlAsync(7, Url, CancellationToken.None);

        var summary = await _downloader.RunAsync(10, CancellationToken.None);

        Assert.Equal(1, summary.NotPermitted);

        // The distinction that matters: this is a settled answer, not something
        // that went wrong. Counting it as a failure would make a source whose
        // file server is closed look, to the alarms in T-15, like a source that
        // is breaking.
        Assert.Equal(0, summary.Failed);

        var document = Assert.Single(_documents.Documents);

        Assert.Equal(0, document.FailedAttempts);
        Assert.Equal("robots.txt disallows it", document.UnavailableReason);

        // And it is gone from the queue immediately - not after three more
        // rounds of asking a server that has already said no.
        Assert.Equal(0, (await _downloader.RunAsync(10, CancellationToken.None)).Considered);
    }

    [Fact]
    public async Task A_file_larger_than_the_limit_is_recorded_but_not_kept()
    {
        const string Url = "https://example.gov.tr/kocaman.zip";
        _fetcher.Returns(Url, new byte[DocumentDownloader.MaximumBytesPerNotice + 1]);
        await _documents.RecordUrlAsync(7, Url, CancellationToken.None);

        await _downloader.RunAsync(10, CancellationToken.None);

        var document = Assert.Single(_documents.Documents);

        Assert.NotNull(document.DownloadedAt);
        Assert.Null(document.StoragePath);
        // Not a failure to retry: it will be just as large next time.
        Assert.Equal(0, document.FailedAttempts);
        Assert.False(Directory.Exists(Path.Combine(_dataDirectory, "doc")));
    }

    [Fact]
    public async Task Once_a_notice_has_spent_its_budget_the_rest_is_skipped()
    {
        var big = new byte[DocumentDownloader.MaximumBytesPerNotice];
        _fetcher.Returns("https://example.gov.tr/bir.bin", big);
        _fetcher.Returns("https://example.gov.tr/iki.bin", SampleFiles.Text("küçük"));

        await _documents.RecordUrlAsync(7, "https://example.gov.tr/bir.bin", CancellationToken.None);
        await _documents.RecordUrlAsync(7, "https://example.gov.tr/iki.bin", CancellationToken.None);

        var summary = await _downloader.RunAsync(10, CancellationToken.None);

        Assert.Equal(1, summary.Downloaded);
        Assert.Equal(1, summary.Skipped);
    }

    [Fact]
    public async Task The_budget_is_per_notice_and_not_shared()
    {
        var big = new byte[DocumentDownloader.MaximumBytesPerNotice];
        _fetcher.Returns("https://example.gov.tr/bir.bin", big);
        _fetcher.Returns("https://example.gov.tr/iki.bin", SampleFiles.Text("küçük"));

        await _documents.RecordUrlAsync(7, "https://example.gov.tr/bir.bin", CancellationToken.None);
        await _documents.RecordUrlAsync(8, "https://example.gov.tr/iki.bin", CancellationToken.None);

        var summary = await _downloader.RunAsync(10, CancellationToken.None);

        Assert.Equal(2, summary.Downloaded);
        Assert.Equal(0, summary.Skipped);
    }

    [Theory]
    [InlineData("../../../etc/passwd", "passwd")]
    [InlineData("C:\\Windows\\system32\\evil.dll", "evil.dll")]
    // Anything unusual becomes a dash, including the Turkish letters. The name
    // the page gave is kept unchanged in notice_document.file_name; this one
    // only decides where the bytes sit on disk, so it is built for safety
    // rather than for looks.
    [InlineData("şartname (1).pdf", "artname-1-.pdf")]
    [InlineData("", "document")]
    [InlineData("...", "document")]
    public void A_file_name_from_a_web_page_cannot_decide_where_it_lands(string given, string expected)
    {
        Assert.Equal(expected, StoragePathBuilder.Sanitize(given));
    }

    [Fact]
    public void A_traversing_file_name_never_escapes_the_archive()
    {
        // The check that actually matters: whatever the sanitiser produces, the
        // resolved path has to stay inside the data directory.
        var path = _paths.DocumentPath(7, "abcdef1234", "../../../etc/passwd");

        Assert.StartsWith(_paths.DataDirectory, _paths.Absolute(path), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private sealed class StubFetcher : IHttpFetcher
    {
        private readonly Dictionary<string, FetchResult> _responses = new(StringComparer.Ordinal);

        public void Returns(string url, byte[] body, string? contentType = "application/octet-stream")
            => _responses[url] = new FetchResult(
                FetchOutcome.Fetched,
                200,
                body,
                ETag: null,
                LastModified: null,
                ContentHash: "hash",
                ContentType: contentType);

        public void Fails(string url) => _responses[url] = FetchResult.Failed(404);

        public void Forbids(string url) => _responses[url] = FetchResult.SkippedByRobots();

        public Task<FetchResult> FetchAsync(FetchRequest request, CancellationToken cancellationToken)
            => Task.FromResult(_responses.GetValueOrDefault(request.Url) ?? FetchResult.Failed(0));
    }
}
