using ProcurementRadar.Domain.Settings;
using System.Text;
using Microsoft.Extensions.Logging;
using ProcurementRadar.Collector;
using ProcurementRadar.Parsers;
using Serilog;
using Serilog.Extensions.Logging;

namespace ProcurementRadar.Cli;

/// <summary>
/// Asks EKAP how one tender ended and prints what the parser makes of the answer.
/// Reads nothing from the database and writes nothing to it.
/// </summary>
/// <remarks>
/// The sweep only asks about tenders whose deadline passed a fortnight ago, so on
/// a young archive it correctly does nothing for weeks - which is a poor way to
/// find out whether it works. This runs the same three steps against any İKN you
/// name, today: fetch the announcement, flatten it, parse it.
///
/// It keeps earning its place afterwards. "Did 2026/1071227 conclude, and at what
/// price" is a question worth being able to ask directly, and the answer arrives
/// without waiting for the sweep to reach it.
/// </remarks>
internal static class AwardProbeCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var ikn = ReadOption(args, "--ikn");

        if (string.IsNullOrWhiteSpace(ikn))
        {
            Log.Error("Usage: award-probe --ikn 2026/1166872 [--save <path>] [--config-dir <path>]");
            return 1;
        }

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

        using var loggerFactory = new SerilogLoggerFactory(Log.Logger);

        var contactEmail = Environment.GetEnvironmentVariable("PR_CONTACT_EMAIL") ?? string.Empty;
        var fetcherOptions = new HttpFetcherOptions { ContactEmail = contactEmail };

        await using var client = new PlaywrightEkapSearchClient(
            new HostPolitenessGate(TimeProvider.System, fetcherOptions),
            fetcherOptions,
            new EkapBrowserOptions
            {
                AnnouncementButtonName = EkapUi.AnnouncementButtonName(ReadOption(args, "--config-dir"))
            },
            loggerFactory.CreateLogger<PlaywrightEkapSearchClient>());

        var result = await client.FetchAnnouncementAsync(ikn, cancellationToken).ConfigureAwait(false);

        if (result.Body is null || result.Outcome != Domain.Fetching.FetchOutcome.Fetched)
        {
            Log.Warning("No announcement came back for {Ikn}", ikn);
            return 1;
        }

        var text = EkapAnnouncementEnricher.PlainText(Encoding.UTF8.GetString(result.Body));

        Log.Information("{Characters} characters of announcement came back", text.Length);

        // --save, because the first thing anybody wants after "won by None" is
        // the announcement the parser was reading, and fetching it again to look
        // costs another trip through the politeness gate. It is also how a result
        // announcement becomes a fixture without a second request, and D-09 will
        // not take a hand-written one.
        //
        // The body as it arrived, not the plain text: that is what the existing
        // fixtures hold and what the tests run PlainText over, so a file saved
        // here can be dropped straight into fixtures/ekap_award. Storing the
        // derived form would bake today's flattening into tomorrow's fixture.
        if (ReadOption(args, "--save") is { Length: > 0 } path)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(path, result.Body, cancellationToken).ConfigureAwait(false);
            Log.Information("Announcement body written to {Path}: {Bytes} bytes", path, result.Body.Length);
        }

        var parser = new AwardParser(new Fixed<AwardLabels>(config.AwardLabels),
            new TurkishDateParser(new Fixed<TurkishDatePatterns>(config.DatePatterns), loggerFactory.CreateLogger<TurkishDateParser>()));

        var award = parser.Parse(text);

        if (award is null)
        {
            // Either the tender is still running or its result is not published
            // yet. Both look the same from here, and both mean the sweep would
            // count an attempt and come back later.
            Log.Information("{Ikn} has no result announcement yet", ikn);
            return 0;
        }

        Log.Information(
            "{Ikn}: won by {Winner} for {Amount} against {Bids} bid(s), contract dated {AwardedAt}",
            ikn,
            award.WinnerName,
            award.ContractAmount,
            award.BidCount,
            award.AwardedAt);

        return 0;
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
