using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProcurementRadar.Collector;
using ProcurementRadar.Persistence;
using Serilog;
using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;

namespace ProcurementRadar.Cli;

/// <summary>
/// Fetches the official announcement for scored EKAP notices and stores its text
/// against them.
/// </summary>
/// <remarks>
/// This is the line between a notification worth reading and one that is not. A
/// title saying "Yazılım ve Destek Hizmeti Alınacaktır" is compatible with an HR
/// system, a licence renewal and a support contract; the announcement names what
/// it is and lists the quantities underneath.
///
/// Only notices at or above the notify threshold are enriched, and only those
/// that carry an İKN and have nothing stored yet. Every announcement costs a
/// browser round trip through the politeness gate, and the ones nobody will ever
/// be told about are not worth one. That is a handful a day rather than ten per
/// poll.
///
/// The text lands as a notice_document row, which is what the message formatter
/// already reads for its excerpt - so nothing downstream changes.
/// </remarks>
internal static class EkapAnnouncementCommand
{
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var connectionString = CliConfiguration.ConnectionString();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Log.Error("PR_DB_CONNECTION_STRING is not set. See docs/decisions.md D-06.");
            return 1;
        }

        var limit = int.TryParse(ReadOption(args, "--limit"), out var parsed) && parsed > 0 ? parsed : 10;

        ConfigFiles config;
        try
        {
            config = ConfigFiles.LoadFrom(ReadOption(args, "--config-dir"));
        }
        catch (Exception exception)
            when (exception is IOException or InvalidOperationException or DirectoryNotFoundException)
        {
            Log.Error(exception, "Could not load the config files");
            return 1;
        }

        PersistenceSetup.RegisterTypeHandlers();

        using var loggerFactory = new SerilogLoggerFactory(Log.Logger);
        await using var connectionFactory = new NpgsqlConnectionFactory(connectionString);

        var documents = new NoticeDocumentRepository(connectionFactory);

        // The announcement carries the buyer's switchboard number and often a
        // named officer. CLAUDE.md says personal data never reaches the database,
        // and an announcement is no exception just because it arrived as one
        // blob of text rather than as a parsed field.
        var masker = new Domain.Normalization.PersonalDataMasker(config.PersonalData);

        await using var client = new PlaywrightEkapSearchClient(
            new HostPolitenessGate(TimeProvider.System, new HttpFetcherOptions
            {
                ContactEmail = Environment.GetEnvironmentVariable("PR_CONTACT_EMAIL") ?? string.Empty
            }),
            new HttpFetcherOptions
            {
                ContactEmail = Environment.GetEnvironmentVariable("PR_CONTACT_EMAIL") ?? string.Empty
            },
            new EkapBrowserOptions
            {
                AnnouncementButtonName = EkapUi.AnnouncementButtonName(ReadOption(args, "--config-dir"))
            },
            loggerFactory.CreateLogger<PlaywrightEkapSearchClient>());

        var pending = await EkapAnnouncementSql
            .ListPendingAsync(connectionFactory, config.Keywords.NotifyThreshold, limit, cancellationToken)
            .ConfigureAwait(false);

        Log.Information("{Count} scored EKAP notice(s) have no announcement text yet", pending.Count);

        var stored = 0;

        foreach (var (noticeId, ikn) in pending)
        {
            var result = await client.FetchAnnouncementAsync(ikn, cancellationToken).ConfigureAwait(false);

            if (result.Body is null || result.Outcome != Domain.Fetching.FetchOutcome.Fetched)
            {
                Log.Warning("No announcement came back for {Ikn}; notice {NoticeId} keeps what it had", ikn, noticeId);
                continue;
            }

            var text = masker.MaskText(PlainText(Encoding.UTF8.GetString(result.Body)));

            if (string.IsNullOrWhiteSpace(text))
            {
                Log.Warning("The announcement for {Ikn} carried no readable text", ikn);
                continue;
            }

            // The url records where this came from. EKAP publishes no address for
            // an announcement - it is drawn into a blank popup - so the search
            // page plus the İKN is the honest answer, and the fragment keeps the
            // row unique without pretending to be a link to the notice.
            await documents
                .RecordUrlAsync(noticeId, $"https://ekapv2.kik.gov.tr/ekap/search#ikn={ikn}", cancellationToken)
                .ConfigureAwait(false);

            await EkapAnnouncementSql
                .StoreTextAsync(connectionFactory, noticeId, ikn, text, cancellationToken)
                .ConfigureAwait(false);

            Log.Information(
                "Stored {Characters} characters of announcement for notice {NoticeId} ({Ikn})",
                text.Length,
                noticeId,
                ikn);

            stored++;
        }

        Log.Information("Announcement run: {Stored} of {Considered} stored", stored, pending.Count);

        return 0;
    }

    /// <summary>
    /// The announcement out of the api envelope, as readable text.
    /// </summary>
    /// <remarks>
    /// veriHtml is a full html document with the tags namespaced, which is of no
    /// use to a keyword filter or to a person reading a phone. Stripped to text
    /// and collapsed, it becomes the paragraph the notification needed.
    /// </remarks>
    private static string PlainText(string body)
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
                if (item.TryGetProperty("veriHtml", out var html) && html.GetString() is { Length: > 0 } value)
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

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
