using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ProcurementRadar.Domain.Fetching;
using ProcurementRadar.Domain.Normalization;
using ProcurementRadar.Domain.Repositories;

namespace ProcurementRadar.Collector;

public sealed record AnnouncementRunSummary(int Considered, int Stored, int Failed);

/// <summary>
/// Fetches the official announcement for notices about to be announced, so the
/// message can say what is actually being bought.
/// </summary>
/// <remarks>
/// A title reading "Yazılım ve Destek Hizmeti Alınacaktır" is compatible with an
/// HR system, a licence renewal and a support contract. The announcement names
/// it - the one behind that title says Microsoft Unified Destek Hizmeti - and
/// carries the table of quantities underneath.
///
/// Runs immediately before the notifier rather than during collection, and only
/// for notices at or above the notify threshold. Every announcement costs a
/// browser round trip through the politeness gate; the ones nobody will ever be
/// told about are not worth one. That is a handful a day instead of ten per poll.
///
/// The text lands as a notice_document row, which is what the message formatter
/// already reads for its excerpt, so nothing downstream had to change.
/// </remarks>
public sealed class EkapAnnouncementEnricher
{
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IEkapSearchClient _search;
    private readonly INoticeDocumentRepository _documents;
    private readonly ProcurementRadar.Domain.Settings.ICurrent<PersonalDataMasker> _masker;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EkapAnnouncementEnricher> _logger;

    public EkapAnnouncementEnricher(
        IEkapSearchClient search,
        INoticeDocumentRepository documents,
        ProcurementRadar.Domain.Settings.ICurrent<PersonalDataMasker> masker,
        TimeProvider timeProvider,
        ILogger<EkapAnnouncementEnricher> logger)
    {
        _search = search;
        _documents = documents;
        _masker = masker;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AnnouncementRunSummary> RunAsync(
        int minimumScore,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var pending = await _documents
            .ListMissingAnnouncementsAsync(minimumScore, batchSize, cancellationToken)
            .ConfigureAwait(false);

        if (pending.Count == 0)
        {
            return new AnnouncementRunSummary(0, 0, 0);
        }

        var stored = 0;
        var failed = 0;

        foreach (var (noticeId, ikn) in pending)
        {
            var result = await _search.FetchAnnouncementAsync(ikn, cancellationToken).ConfigureAwait(false);

            if (result.Outcome != FetchOutcome.Fetched || result.Body is null)
            {
                // Not fatal and not retried in a loop: the next run will try
                // again, and the notification goes out with what it has rather
                // than waiting for EKAP to feel better.
                _logger.LogWarning(
                    "No announcement came back for {Ikn}; notice {NoticeId} will be announced without one",
                    ikn,
                    noticeId);

                failed++;
                continue;
            }

            // The announcement carries the buying authority's switchboard number
            // and often a named officer. Personal data never reaches the database
            // (CLAUDE.md), and arriving as one blob of text rather than a parsed
            // field does not make it an exception.
            var text = _masker.Value.MaskText(PlainText(Encoding.UTF8.GetString(result.Body)));

            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("The announcement for {Ikn} carried no readable text", ikn);
                failed++;
                continue;
            }

            await _documents.StoreAnnouncementAsync(
                noticeId,
                // Where it came from. EKAP publishes no address for an
                // announcement - it is drawn into a blank popup - so the search
                // page and the İKN is the honest answer, and the fragment keeps
                // the row unique without pretending to be a link to the notice.
                $"https://ekapv2.kik.gov.tr/ekap/search#ikn={ikn}",
                $"ilan-{ikn.Replace('/', '-')}.txt",
                text,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Stored {Characters} characters of announcement for notice {NoticeId} ({Ikn})",
                text.Length,
                noticeId,
                ikn);

            stored++;
        }

        _logger.LogInformation(
            "Announcement run: {Considered} considered, {Stored} stored, {Failed} without one",
            pending.Count,
            stored,
            failed);

        return new AnnouncementRunSummary(pending.Count, stored, failed);
    }

    /// <summary>
    /// The announcement out of the api envelope, as readable text.
    /// </summary>
    /// <remarks>
    /// veriHtml is a whole html document with namespaced tags, which is no use to
    /// a keyword filter or to somebody reading a phone. Stripped and collapsed it
    /// becomes the paragraph the notification needed.
    /// </remarks>
    public static string PlainText(string body)
    {
        try
        {
            var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("list", out var list)
                || list.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();

            foreach (var item in list.EnumerateArray())
            {
                if (item.TryGetProperty("veriHtml", out var html)
                    && html.GetString() is { Length: > 0 } value)
                {
                    builder.AppendLine(Whitespace.Replace(Tags.Replace(value, " "), " ").Trim());
                }
            }

            return System.Net.WebUtility.HtmlDecode(builder.ToString()).Trim();
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
