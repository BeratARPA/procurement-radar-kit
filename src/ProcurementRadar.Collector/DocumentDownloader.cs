using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Fetching;
using ProcurementRadar.Domain.Normalization;
using ProcurementRadar.Domain.Repositories;

namespace ProcurementRadar.Collector;

public sealed record DocumentRunSummary(
    int Considered,
    int Downloaded,
    int Failed,
    int Skipped,
    int NotPermitted = 0);

/// <summary>
/// Fetches the specification files a notice links to, keeps them, and pulls the
/// text out.
/// </summary>
/// <remarks>
/// Runs on its own, away from the parse pipeline, because it is the one part of
/// document handling that reaches the network - and it does so through the same
/// fetcher as everything else, so specification downloads queue behind the same
/// per-host limits as page polling.
/// </remarks>
public sealed class DocumentDownloader
{
    /// <summary>Total bytes kept for one notice. Beyond this the rest is skipped.</summary>
    public const long MaximumBytesPerNotice = 50L * 1024 * 1024;

    /// <summary>Failures before a url is left alone.</summary>
    public const int MaximumAttempts = 3;

    private readonly IHttpFetcher _fetcher;
    private readonly INoticeDocumentRepository _documents;
    private readonly StoragePathBuilder _paths;
    private readonly DocumentTextExtractor _extractor;
    private readonly ProcurementRadar.Domain.Settings.ICurrent<PersonalDataMasker> _masker;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DocumentDownloader> _logger;

    public DocumentDownloader(
        IHttpFetcher fetcher,
        INoticeDocumentRepository documents,
        StoragePathBuilder paths,
        DocumentTextExtractor extractor,
        ProcurementRadar.Domain.Settings.ICurrent<PersonalDataMasker> masker,
        TimeProvider timeProvider,
        ILogger<DocumentDownloader> logger)
    {
        _fetcher = fetcher;
        _documents = documents;
        _paths = paths;
        _extractor = extractor;
        _masker = masker;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<DocumentRunSummary> RunAsync(int batchSize, CancellationToken cancellationToken)
    {
        var pending = await _documents
            .ListPendingAsync(MaximumAttempts, batchSize, cancellationToken)
            .ConfigureAwait(false);

        var downloaded = 0;
        var failed = 0;
        var skipped = 0;
        var notPermitted = 0;

        // Counted per notice, not per run: the limit in the ticket is what one
        // notice may cost on disk.
        var bytesPerNotice = new Dictionary<long, long>();

        foreach (var document in pending)
        {
            var used = bytesPerNotice.GetValueOrDefault(document.NoticeId);

            if (used >= MaximumBytesPerNotice)
            {
                _logger.LogWarning(
                    "Notice {NoticeId} has already used its {Limit} byte document budget; skipping {Url}",
                    document.NoticeId,
                    MaximumBytesPerNotice,
                    document.Url);

                skipped++;
                continue;
            }

            var (outcome, size) = await DownloadAsync(document, cancellationToken).ConfigureAwait(false);

            switch (outcome)
            {
                case DocumentOutcome.Failed:
                    failed++;
                    continue;

                case DocumentOutcome.NotPermitted:
                    notPermitted++;
                    continue;

                default:
                    bytesPerNotice[document.NoticeId] = used + size;
                    downloaded++;
                    break;
            }
        }

        _logger.LogInformation(
            "Document run: {Considered} considered, {Downloaded} downloaded, {Failed} failed, "
                + "{Skipped} skipped, {NotPermitted} not permitted",
            pending.Count,
            downloaded,
            failed,
            skipped,
            notPermitted);

        return new DocumentRunSummary(pending.Count, downloaded, failed, skipped, notPermitted);
    }

    private enum DocumentOutcome
    {
        Downloaded,
        Failed,

        /// <summary>Settled, not retried: robots.txt puts this file out of bounds.</summary>
        NotPermitted
    }

    private async Task<(DocumentOutcome Outcome, long Bytes)> DownloadAsync(
        NoticeDocument document,
        CancellationToken cancellationToken)
    {
        var result = await _fetcher
            .FetchAsync(new FetchRequest(document.Url), cancellationToken)
            .ConfigureAwait(false);

        if (result.Outcome == FetchOutcome.SkippedByRobots)
        {
            // Not a failure. The server has told us not to, we did not ask, and
            // asking twice more would neither change the answer nor be polite.
            // Written down once so the health counters in T-15 do not read a
            // correctly behaving system as a breaking one - and so a person can
            // see why a notice has no specification attached to it.
            _logger.LogWarning(
                "robots.txt puts {Url} out of bounds; notice {NoticeId} will have no copy of it",
                document.Url,
                document.NoticeId);

            await _documents
                .MarkUnavailableAsync(document.Id, "robots.txt disallows it", cancellationToken)
                .ConfigureAwait(false);

            return (DocumentOutcome.NotPermitted, 0);
        }

        if (result.Outcome != FetchOutcome.Fetched || result.Body is null)
        {
            _logger.LogWarning(
                "Could not fetch {Url} for notice {NoticeId}: {Outcome}",
                document.Url,
                document.NoticeId,
                result.Outcome);

            await _documents.RecordFailureAsync(document.Id, cancellationToken).ConfigureAwait(false);

            return (DocumentOutcome.Failed, 0);
        }

        if (result.Body.LongLength > MaximumBytesPerNotice)
        {
            _logger.LogWarning(
                "{Url} is {Size} bytes, over the {Limit} byte limit; not keeping it",
                document.Url,
                result.Body.LongLength,
                MaximumBytesPerNotice);

            // Not a failure to retry: it will be just as large next time.
            await _documents.MarkDownloadedAsync(
                document with
                {
                    FileName = FileNameOf(document.Url),
                    MimeType = result.ContentType,
                    SizeBytes = result.Body.LongLength,
                    DownloadedAt = _timeProvider.GetUtcNow()
                },
                cancellationToken).ConfigureAwait(false);

            return (DocumentOutcome.Downloaded, result.Body.LongLength);
        }

        var fileName = FileNameOf(document.Url);
        var hash = Convert.ToHexString(SHA256.HashData(result.Body)).ToLowerInvariant();
        var relativePath = _paths.DocumentPath(document.NoticeId, hash, fileName);
        var absolutePath = _paths.Absolute(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        await File.WriteAllBytesAsync(absolutePath, result.Body, cancellationToken).ConfigureAwait(false);

        var extracted = _extractor.Extract(result.Body, fileName);

        await _documents.MarkDownloadedAsync(
            document with
            {
                FileName = fileName,
                MimeType = result.ContentType,
                SizeBytes = result.Body.LongLength,
                StoragePath = relativePath,
                DownloadedAt = _timeProvider.GetUtcNow(),
                // Masked before it is stored, not after.
                //
                // This was missing outright. spec.md Bölüm 12 and CLAUDE.md both
                // say personal data must never reach the database, and the
                // announcement path had a masker while this one - the one that
                // reads whole şartname pdfs - wrote the extracted text through
                // untouched. Measured before the fix: of 270 stored texts, 188
                // carried an email address and 17 carried something shaped like
                // a phone number.
                //
                // The bytes on disk are left exactly as downloaded. They are the
                // archive, and D-11's rule is that the archive holds what
                // arrived; what is masked is the copy that goes into a column
                // and gets read by a screen.
                ExtractedText = _masker.Value.MaskText(extracted.Text)
            },
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Kept {FileName} for notice {NoticeId} ({Size} bytes, {Kind}, {Characters} characters of text)",
            fileName,
            document.NoticeId,
            result.Body.LongLength,
            extracted.Kind,
            extracted.Text?.Length ?? 0);

        return (DocumentOutcome.Downloaded, result.Body.LongLength);
    }

    private static string FileNameOf(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? Path.GetFileName(uri.LocalPath)
            : string.Empty;
}
