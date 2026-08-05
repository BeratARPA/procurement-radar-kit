using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Fetching;
using ProcurementRadar.Parsers;

namespace ProcurementRadar.Parsers.Tests;

/// <summary>
/// Runs <see cref="GenericTableAdapter"/> against every captured page under
/// fixtures/generic_table.
/// </summary>
/// <remarks>
/// A fixture is a real page saved from a live site, next to a .json file saying
/// what the adapter should make of it:
/// <code>
/// fixtures/generic_table/cankaya-belediyesi.html
/// fixtures/generic_table/cankaya-belediyesi.json
/// {
///   "adapter_config": {
///     "row_selector": "table.ihale tbody tr",
///     "title_selector": "td:nth-child(2)",
///     "link_selector": "a",
///     "date_selector": "td:nth-child(1)",
///     "amount_selector": null,
///     "base_url": "https://www.cankaya.bel.tr"
///   },
///   "expected_notice_count": 12,
///   "expected_first_title": "Yazılım Bakım ve Destek Hizmeti Alımı"
/// }
/// </code>
/// Hand-written HTML is not accepted: it tests the assumption, not the site.
/// </remarks>
public sealed class GenericTableAdapterFixtureTests
{
    private static readonly string FixtureDirectory =
        Path.Combine(AppContext.BaseDirectory, "fixtures", GenericTableAdapter.AdapterKey);

    // The real parser, loaded from the same config file the worker uses. A
    // fixture that cannot have its dates read should say so here.
    private static readonly TurkishDatePatterns DatePatterns = TurkishDatePatterns.Load(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config", "tr-date-patterns.json")));

    [Fact]
    public void Every_captured_page_parses_to_the_number_of_notices_it_should()
    {
        foreach (var htmlPath in CapturedPages())
        {
            var expectationPath = Path.ChangeExtension(htmlPath, ".json");

            Assert.True(
                File.Exists(expectationPath),
                $"{Path.GetFileName(htmlPath)} has no .json beside it saying what it should parse to.");

            var expectation = JsonSerializer.Deserialize<Expectation>(File.ReadAllText(expectationPath))!;
            var source = SourceFor(expectation);
            var adapter = new GenericTableAdapter(
                new UnusableFetcher(),
                new TurkishDateParser(DatePatterns, NullLogger<TurkishDateParser>.Instance),
                NullLogger<GenericTableAdapter>.Instance);

            var notices = adapter.Parse(File.ReadAllText(htmlPath), source);

            Assert.Equal(expectation.ExpectedNoticeCount, notices.Count);

            if (expectation.ExpectedFirstTitle is { Length: > 0 } firstTitle)
            {
                Assert.Equal(firstTitle, notices[0].Title);
            }

            if (expectation.ExpectedFirstDeadline is { Length: > 0 } firstDeadline)
            {
                Assert.Equal(
                    DateTimeOffset.Parse(firstDeadline, System.Globalization.CultureInfo.InvariantCulture),
                    notices[0].BidDeadlineAt);
            }

            if (expectation.ExpectedFirstDocumentCount is { } documentCount)
            {
                Assert.Equal(documentCount, notices[0].DocumentUrls.Count);
            }

            foreach (var notice in notices)
            {
                Assert.False(string.IsNullOrWhiteSpace(notice.Title));
                Assert.True(
                    Uri.TryCreate(notice.SourceUrl, UriKind.Absolute, out _),
                    $"{notice.SourceUrl} is relative; a stored relative link is useless once the listing changes.");
            }
        }
    }

    [Fact]
    public void Every_captured_page_is_still_the_bytes_the_server_sent()
    {
        // A fixture is evidence, and evidence that has been edited is no longer
        // evidence. This is not hypothetical: git's line-ending normalisation was
        // storing one of these pages as 126 263 bytes where the server had sent
        // 129 075, and because Windows converted them back on checkout, nothing
        // looked wrong locally. .gitattributes now marks the fixture directory
        // -text; this test is what notices if that ever stops being true.
        foreach (var htmlPath in CapturedPages())
        {
            var expectationPath = Path.ChangeExtension(htmlPath, ".json");
            var expectation = JsonSerializer.Deserialize<Expectation>(File.ReadAllText(expectationPath))!;

            if (expectation.CapturedSha256 is not { Length: > 0 } expectedHash)
            {
                continue;
            }

            var content = File.ReadAllBytes(htmlPath);
            var name = Path.GetFileName(htmlPath);
            var actualHash = Convert
                .ToHexString(System.Security.Cryptography.SHA256.HashData(content))
                .ToLowerInvariant();

            Assert.True(
                expectation.CapturedBytes == content.LongLength && expectedHash == actualHash,
                $"{name} is no longer the page that was captured from the live site. "
                    + $"Expected {expectation.CapturedBytes} bytes / {expectedHash}, "
                    + $"found {content.LongLength} bytes / {actualHash}. "
                    + "If the site changed, add the new page as a second fixture rather than "
                    + "overwriting this one - the archive still holds pages in the old shape.");
        }
    }

    private static IEnumerable<string> CapturedPages()
        => Directory.Exists(FixtureDirectory)
            ? Directory.EnumerateFiles(FixtureDirectory, "*.html").Order(StringComparer.Ordinal)
            : [];

    private static Source SourceFor(Expectation expectation) => new()
    {
        Id = 1,
        AuthorityName = "Fixture Authority",
        AuthorityType = AuthorityType.Municipality,
        Province = "Ankara",
        Url = "https://fixture.invalid/ihaleler",
        AdapterKey = GenericTableAdapter.AdapterKey,
        AdapterConfig = expectation.AdapterConfig.GetRawText()
    };

    private sealed record Expectation
    {
        [JsonPropertyName("adapter_config")]
        public JsonElement AdapterConfig { get; init; }

        [JsonPropertyName("expected_notice_count")]
        public int ExpectedNoticeCount { get; init; }

        [JsonPropertyName("expected_first_title")]
        public string? ExpectedFirstTitle { get; init; }

        [JsonPropertyName("expected_first_deadline_utc")]
        public string? ExpectedFirstDeadline { get; init; }

        [JsonPropertyName("expected_first_document_count")]
        public int? ExpectedFirstDocumentCount { get; init; }

        /// <summary>Size of the page as the server sent it. Optional, but paired with the hash.</summary>
        [JsonPropertyName("captured_bytes")]
        public long CapturedBytes { get; init; }

        [JsonPropertyName("captured_sha256")]
        public string? CapturedSha256 { get; init; }
    }

    /// <summary>Parse must never reach the network, so this would throw if it did.</summary>
    private sealed class UnusableFetcher : IHttpFetcher
    {
        public Task<FetchResult> FetchAsync(FetchRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Parse must not fetch anything.");
    }
}
