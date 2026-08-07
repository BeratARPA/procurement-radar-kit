using ProcurementRadar.Domain.Settings;
using Microsoft.Extensions.Logging;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Parsers;
using Serilog;
using Serilog.Extensions.Logging;

namespace ProcurementRadar.Cli;

/// <summary>
/// Runs an adapter over a saved page and reports what it made of it, without a
/// database and without touching the network.
/// </summary>
/// <remarks>
/// This is step 4 of the source-adding procedure in spec Bölüm 5. Adding a
/// source means writing CSS selectors against somebody else's markup, and until
/// now the only way to find out whether they were right was to seed the source
/// and wait for a poll - which either produces notices or produces nothing, and
/// nothing is exactly what a healthy source with no work out this week looks
/// like. This answers the question offline, in a second, against the page as it
/// was saved.
///
/// It reads bytes and decodes them the way the parse pipeline does (D-11), so a
/// page served as windows-1254 is judged the same way here as in production.
/// </remarks>
internal static class CheckSourceCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var htmlPath = ReadOption(args, "--html");
        var configPath = ReadOption(args, "--config");

        if (htmlPath is null || configPath is null)
        {
            Log.Error(
                "check-source needs --html <saved page> and --config <adapter_config json>. "
                + "Optionally --url <the page's own url>, --charset <from the http header>.");

            return 1;
        }

        if (!File.Exists(htmlPath))
        {
            Log.Error("No such file: {Path}", htmlPath);
            return 1;
        }

        if (!File.Exists(configPath))
        {
            Log.Error("No such file: {Path}", configPath);
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

        var adapterConfig = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);

        if (!GenericTableAdapterConfig.TryRead(adapterConfig, out _, out var problem))
        {
            // Worth failing here rather than parsing to zero rows and leaving the
            // reader to guess which of the two went wrong.
            Log.Error("The adapter config is not usable: {Problem}", problem);
            return 1;
        }

        var bytes = await File.ReadAllBytesAsync(htmlPath, cancellationToken).ConfigureAwait(false);
        var (body, charSet) = BodyDecoder.Decode(bytes, ReadOption(args, "--charset"));

        using var loggerFactory = new SerilogLoggerFactory(Log.Logger);

        var adapter = new GenericTableAdapter(
            new UnusableFetcher(),
            new TurkishDateParser(new Fixed<TurkishDatePatterns>(config.DatePatterns), loggerFactory.CreateLogger<TurkishDateParser>()),
            loggerFactory.CreateLogger<GenericTableAdapter>());

        var source = new Source
        {
            Id = 0,
            AuthorityName = "check-source",
            AuthorityType = AuthorityType.Municipality,
            Province = "Ankara",
            Url = ReadOption(args, "--url") ?? "https://check-source.invalid/",
            AdapterKey = GenericTableAdapter.AdapterKey,
            AdapterConfig = adapterConfig
        };

        var notices = adapter.Parse(body, source);

        Log.Information(
            "{Path}: {Bytes} bytes, decoded as {CharSet}, parsed to {Count} notice(s)",
            Path.GetFileName(htmlPath),
            bytes.Length,
            charSet,
            notices.Count);

        if (notices.Count == 0)
        {
            // The whole point of the command. Say it plainly.
            Log.Warning(
                "No notices came out of {Path}. Either the selectors do not match this markup, "
                + "or the page genuinely has no notices on it right now. Look at the page before "
                + "activating the source.",
                Path.GetFileName(htmlPath));

            return 2;
        }

        var withoutDeadline = 0;

        foreach (var notice in notices)
        {
            if (notice.BidDeadlineAt is null)
            {
                withoutDeadline++;
            }
        }

        var first = notices[0];

        Log.Information(
            "First notice: {Title} | deadline {Deadline} | url {Url} | {Documents} document(s)",
            first.Title,
            first.BidDeadlineAt?.ToString("u") ?? "not read",
            first.SourceUrl,
            first.DocumentUrls.Count);

        Log.Information(
            "Across all {Count}: {WithDeadline} with a deadline, {WithoutDeadline} without, "
            + "{WithDocuments} linking documents",
            notices.Count,
            notices.Count - withoutDeadline,
            withoutDeadline,
            notices.Count(notice => notice.DocumentUrls.Count > 0));

        if (withoutDeadline == notices.Count)
        {
            // Not fatal - spec Bölüm 7 says record the notice anyway - but a
            // whole page with no readable date usually means the date selector
            // points at the wrong cell rather than that the authority omitted it.
            Log.Warning(
                "Not one deadline could be read. Check date_selector against the markup, "
                + "and check that the format is in config/tr-date-patterns.json.");
        }

        return 0;
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

    /// <summary>Parse is a pure function; this makes a stray request impossible.</summary>
    private sealed class UnusableFetcher : Domain.Fetching.IHttpFetcher
    {
        public Task<Domain.Fetching.FetchResult> FetchAsync(
            Domain.Fetching.FetchRequest request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("check-source reads a file; it does not fetch.");
    }
}
