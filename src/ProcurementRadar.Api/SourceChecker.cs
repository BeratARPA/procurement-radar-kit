using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ProcurementRadar.Api.Contracts;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Fetching;
using ProcurementRadar.Domain.Repositories;
using ProcurementRadar.Domain.Settings;
using ProcurementRadar.Parsers;

namespace ProcurementRadar.Api;

/// <summary>
/// Runs a set of selectors over a page and reports what they made of it.
/// </summary>
/// <remarks>
/// The `check-source` command with a screen in front of it. spec Bolum 5 makes
/// this step 4 of adding a source, and it is the step that used to have no
/// answer: writing CSS selectors against somebody else's markup and finding out
/// whether they were right by switching the source on and waiting. That produces
/// either notices or nothing, and nothing is exactly what a working source with
/// a quiet week looks like.
///
/// Two rules hold here as everywhere else. Nothing is fetched - the page comes
/// from the archive or from the request, so trying a selector costs the
/// authority nothing and cannot be rate limited. And bytes are decoded the way
/// the parse pipeline decodes them (D-11), so a page served as windows-1254 is
/// judged here the way it will be judged in production.
/// </remarks>
public sealed class SourceChecker
{
    private readonly ISourceRepository _sources;
    private readonly IRawFetchRepository _fetches;
    private readonly IRawArchive _archive;
    private readonly ISettingRepository _settings;

    public SourceChecker(
        ISourceRepository sources,
        IRawFetchRepository fetches,
        IRawArchive archive,
        ISettingRepository settings)
    {
        _sources = sources;
        _fetches = fetches;
        _archive = archive;
        _settings = settings;
    }

    public async Task<SourceCheckResponse> CheckAsync(
        SourceCheckRequest request,
        CancellationToken cancellationToken)
    {
        if (!GenericTableAdapterConfig.TryRead(request.AdapterConfig, out _, out var problem))
        {
            // Said here rather than parsed to zero rows. Zero rows from an
            // unusable config and zero rows from selectors that miss look
            // identical on a screen, and they need different fixes.
            //
            // The parser's own sentence is passed through beside the code. It
            // names the offending field, which no code could, and a person
            // writing selectors is the audience for it.
            return Empty(CheckProblems.ConfigUnusable, "config") with { Problem = problem };
        }

        byte[] bytes;
        string origin;
        DateTimeOffset? fetchedAt = null;
        string? declaredCharSet = null;

        if (!string.IsNullOrWhiteSpace(request.Html))
        {
            // A page pasted in, for a source that has never been fetched. Read
            // as UTF-8 because that is what a browser hands over on copy; the
            // encoding question only exists for bytes off the wire.
            bytes = Encoding.UTF8.GetBytes(request.Html);
            origin = "pasted";
        }
        else if (request.SourceId is { } sourceId)
        {
            var latest = await _fetches.GetLatestAsync(sourceId, cancellationToken).ConfigureAwait(false);

            if (latest is null)
            {
                return Empty(problemCode: CheckProblems.NoArchivedPage, origin: "archive");
            }

            bytes = await _archive.ReadAsync(latest.BodyPath, cancellationToken).ConfigureAwait(false);
            origin = "archive";
            fetchedAt = latest.FetchedAt;
            declaredCharSet = CharSetFrom(latest.ContentType);
        }
        else
        {
            return Empty(problemCode: CheckProblems.NothingToCheck, origin: "none");
        }

        var (body, charSet) = BodyDecoder.Decode(bytes, declaredCharSet);

        var patterns = await _settings.GetAsync(SettingKeys.DatePatterns, cancellationToken).ConfigureAwait(false);

        // The same date parser the pipeline uses, on the same patterns. A check
        // that read dates by different rules would pass configs that then fail.
        var dateParser = new TurkishDateParser(
            new Fixed<TurkishDatePatterns>(TurkishDatePatterns.Load(
                patterns?.Value ?? ConfigDirectory.Read(ConfigDirectory.Find(), "tr-date-patterns.json"))),
            NullLogger<TurkishDateParser>.Instance);

        var adapter = new GenericTableAdapter(
            new UnusableFetcher(),
            dateParser,
            NullLogger<GenericTableAdapter>.Instance);

        var source = await DescribeAsync(request, cancellationToken).ConfigureAwait(false);

        var notices = adapter.Parse(body, source);

        var withDeadline = notices.Count(notice => notice.BidDeadlineAt is not null);
        var withDocuments = notices.Count(notice => notice.DocumentUrls.Count > 0);

        return new SourceCheckResponse
        {
            Source = origin,
            FetchedAt = fetchedAt,
            CharSet = charSet,
            ByteCount = bytes.Length,
            NoticeCount = notices.Count,
            WithDeadline = withDeadline,
            WithDocuments = withDocuments,
            Sample = notices.Take(10).Select(notice => new CheckedNotice
            {
                Title = notice.Title,
                SourceUrl = notice.SourceUrl,
                BidDeadlineAt = notice.BidDeadlineAt,
                DocumentCount = notice.DocumentUrls.Count
            }).ToList(),
            Warnings = Warnings(notices.Count, withDeadline)
        };
    }

    /// <summary>
    /// What the parser needs to know about where the page came from.
    /// </summary>
    /// <remarks>
    /// The url matters: relative links on the page are resolved against it, so
    /// checking with the wrong one produces the right titles and the wrong
    /// addresses. Taken from the source being edited when there is one.
    /// </remarks>
    private async Task<Source> DescribeAsync(
        SourceCheckRequest request,
        CancellationToken cancellationToken)
    {
        var existing = request.SourceId is { } id
            ? await _sources.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            : null;

        return new Source
        {
            Id = existing?.Id ?? 0,
            AuthorityName = existing?.AuthorityName ?? "check",
            AuthorityType = existing?.AuthorityType ?? AuthorityType.Municipality,
            Province = existing?.Province ?? "Ankara",
            Url = request.Url ?? existing?.Url ?? "https://check.invalid/",
            AdapterKey = GenericTableAdapter.AdapterKey,
            AdapterConfig = request.AdapterConfig
        };
    }

    private static IReadOnlyList<string> Warnings(int count, int withDeadline)
    {
        var warnings = new List<string>();

        if (count == 0)
        {
            warnings.Add(CheckWarnings.NoRows);

            return warnings;
        }

        if (withDeadline == 0)
        {
            // Not fatal - spec Bolum 7 says write the notice anyway - but a whole
            // page with no readable date is usually a date selector pointing at
            // the wrong cell rather than an authority that omits dates.
            warnings.Add(CheckWarnings.NoDeadlineRead);
        }
        else if (withDeadline < count / 2)
        {
            warnings.Add(CheckWarnings.FewDeadlinesRead);
        }

        return warnings;
    }

    private static SourceCheckResponse Empty(string problemCode, string origin) => new()
    {
        ProblemCode = problemCode,
        Source = origin,
        ByteCount = 0,
        NoticeCount = 0,
        WithDeadline = 0,
        WithDocuments = 0,
        Sample = [],
        Warnings = []
    };

    /// <summary>
    /// The charset the server declared, kept apart from the bytes on purpose.
    /// </summary>
    /// <remarks>
    /// D-11: the header is not in the body, so it cannot be recovered from the
    /// archive file. `raw_fetch.content_type` is where it was put instead.
    /// </remarks>
    private static string? CharSetFrom(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;

        var marker = contentType.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);

        if (marker < 0) return null;

        var value = contentType[(marker + "charset=".Length)..].Trim().Trim('"');
        var end = value.IndexOf(';');

        return end < 0 ? value : value[..end].Trim();
    }

    /// <summary>Parse is a pure function; this makes a stray request impossible.</summary>
    private sealed class UnusableFetcher : IHttpFetcher
    {
        public Task<FetchResult> FetchAsync(FetchRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("A check reads a saved page; it does not fetch.");
    }
}
