using System.Net;
using System.Text;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Adapters;
using ProcurementRadar.Domain.Fetching;
using ProcurementRadar.Domain.Normalization;
using ProcurementRadar.Domain.Parsing;
using ProcurementRadar.Domain.Repositories;
using ProcurementRadar.Domain.Health;
using ProcurementRadar.Domain.Notifications;
using ProcurementRadar.Domain.Scoring;
using ProcurementRadar.Domain.Settings;
using ProcurementRadar.Parsers;
using ProcurementRadar.Notifier;
using ProcurementRadar.Persistence;
using ProcurementRadar.Scoring;
using ProcurementRadar.Worker;
using Serilog;

// The console sink writes JSON so the logs can be grepped and fed to tools
// (D-07). On Windows the console defaults to the system code page and .NET's
// encoder for one substitutes a best fit: a Turkish “ in a notice title becomes
// a plain ", which turns that log line into invalid JSON, and ş, ğ and İ all
// arrive as question marks. The file sink writes correct UTF-8 either way, which
// is exactly what makes the console version easy to miss.
Console.OutputEncoding = Encoding.UTF8;

// D-06 names .env as how secrets reach a running worker. Loaded before the host
// is built, because the connection string is read while it is being configured.
// Anything already in the environment wins, so systemd's EnvironmentFile and a
// deliberate export both still take precedence.
var envFile = DotEnv.Load();

var builder = Host.CreateApplicationBuilder(args);

// D-07: compact JSON on the console, daily rolling file on disk. Sinks and
// levels live in appsettings.json so they can be changed without a rebuild.
// The default providers are cleared first, otherwise every line is written
// twice: once by Serilog and once by the built-in console logger.
builder.Logging.ClearProviders();
builder.Services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services));

// D-06: user secrets while developing, environment variables when running for
// real. Missing settings stop the host now rather than at the first request.
var connectionString = Required(builder.Configuration, "PR_DB_CONNECTION_STRING");
var contactEmail = Required(builder.Configuration, "PR_CONTACT_EMAIL");
var dataDirectory = builder.Configuration["PR_DATA_DIR"] ?? StoragePathBuilder.DefaultDataDirectory;

// The Turkish data files. Read once, here, so nothing on the parse path has to
// touch the disk. The directory is searched for rather than assumed: the two
// hosts and systemd all start with a different working directory.
var configDirectory = ConfigDirectory.Find();
var datePatterns = TurkishDatePatterns.Load(ReadConfig("tr-date-patterns.json"));
var normalization = NormalizationRules.Load(ReadConfig("tr-normalization.json"));
var personalData = PersonalDataRules.Load(ReadConfig("tr-personal-data.json"));
var keywords = KeywordRules.Load(ReadConfig("keywords.json"));
var messages = MessageTemplates.Load(ReadConfig("tr-messages.json"));
var awardLabels = AwardLabels.Load(ReadConfig("tr-award-labels.json"));
var politeness = PolitenessSettings.Load(ReadConfig("politeness.json"));

PersistenceSetup.RegisterTypeHandlers();

builder.Services.AddSingleton(TimeProvider.System);

// The politeness budget is a setting now, with floors it cannot cross
// (D-16). Read once at startup rather than live: a five-second gap that
// becomes six on the next restart costs nothing, and a gate that changes
// its mind between two requests to the same host is harder to reason
// about than one that does not.
builder.Services.AddSingleton(politeness.ToFetcherOptions(contactEmail));
builder.Services.AddSingleton(_ => new HttpClient(new SocketsHttpHandler
{
    // One long-lived client rather than one per request, with a connection
    // lifetime short enough that DNS changes are picked up.
    PooledConnectionLifetime = TimeSpan.FromMinutes(15),
    AutomaticDecompression = DecompressionMethods.All
}));
builder.Services.AddSingleton<IHttpFetcher, HttpFetcher>();

builder.Services.AddSingleton(new StoragePathBuilder(dataDirectory));
builder.Services.AddSingleton<IRawArchive, FileSystemRawArchive>();
builder.Services.AddSingleton<RawFetchRecorder>();
builder.Services.AddSingleton<PollIntervalCalculator>();

builder.Services.AddSingleton<IDbConnectionFactory>(_ => new NpgsqlConnectionFactory(connectionString));
builder.Services.AddSingleton<ISourceRepository, SourceRepository>();
builder.Services.AddSingleton<IRawFetchRepository, RawFetchRepository>();
builder.Services.AddSingleton<INoticeRepository, NoticeRepository>();
builder.Services.AddSingleton<INoticeScoreRepository, NoticeScoreRepository>();
builder.Services.AddSingleton<INoticeDocumentRepository, NoticeDocumentRepository>();
builder.Services.AddSingleton<INotificationRepository, NotificationRepository>();
builder.Services.AddSingleton<IHealthRepository, HealthRepository>();
builder.Services.AddSingleton<IAwardRepository, AwardRepository>();

builder.Services.AddSingleton<ITurkishDateParser, TurkishDateParser>();
builder.Services.AddSingleton(new NoticeNormalizer(normalization));
builder.Services.AddSingleton<ExternalKeyBuilder>();
builder.Services.AddSingleton(keywords);
builder.Services.AddSingleton<INoticeScorer, RuleScorer>();

// The settings the interface will edit (D-16). The value read from the config
// file is the fallback, used until the database answers - a worker that cannot
// reach the setting table yet still collects, parses and scores rather than
// refusing to start.
builder.Services.AddSingleton<ISettingRepository, SettingRepository>();
builder.Services.AddSingleton(services => new SettingSeeder(
    services.GetRequiredService<ISettingRepository>(),
    configDirectory,
    services.GetRequiredService<ILogger<SettingSeeder>>()));

var liveKeywords = new LiveSetting<KeywordRules>(SettingKeys.Keywords, KeywordRules.Load, keywords);
var liveMessages = new LiveSetting<MessageTemplates>(SettingKeys.Messages, MessageTemplates.Load, messages);
var liveDates = new LiveSetting<TurkishDatePatterns>(
    SettingKeys.DatePatterns, TurkishDatePatterns.Load, datePatterns);
var liveAwardLabels = new LiveSetting<AwardLabels>(SettingKeys.AwardLabels, AwardLabels.Load, awardLabels);
var livePoliteness = new LiveSetting<PolitenessSettings>(
    SettingKeys.Politeness, PolitenessSettings.Load, politeness);

// The masker rather than the rules: it compiles its patterns once, and the
// thing every caller wants is the compiled object.
var liveMasker = new LiveSetting<PersonalDataMasker>(
    SettingKeys.PersonalData,
    json => new PersonalDataMasker(PersonalDataRules.Load(json)),
    new PersonalDataMasker(personalData));

foreach (var live in new LiveSetting[]
         { liveKeywords, liveMessages, liveDates, liveAwardLabels, liveMasker, livePoliteness })
{
    builder.Services.AddSingleton(live);
}

builder.Services.AddSingleton<ICurrent<KeywordRules>>(liveKeywords);
builder.Services.AddSingleton<ICurrent<MessageTemplates>>(liveMessages);
builder.Services.AddSingleton<ICurrent<TurkishDatePatterns>>(liveDates);
builder.Services.AddSingleton<ICurrent<AwardLabels>>(liveAwardLabels);
builder.Services.AddSingleton<ICurrent<PersonalDataMasker>>(liveMasker);
builder.Services.AddSingleton<ICurrent<PolitenessSettings>>(livePoliteness);

builder.Services.AddSingleton<SettingsRefresher>();
builder.Services.AddHostedService<SettingsWorker>();

// The browser is already here for EKAP; this lends it to the handful of pages
// whose listings are written by javascript (spec section 13).
builder.Services.AddSingleton<PlaywrightPageRenderer>();
builder.Services.AddSingleton<IPageRenderer>(services =>
    services.GetRequiredService<PlaywrightPageRenderer>());
builder.Services.AddSingleton<ISourceAdapter, GenericTableAdapter>();
// Listeyi sayfa yerine json olarak veren kaynaklar icin.
builder.Services.AddSingleton<ISourceAdapter, JsonListAdapter>();

// EKAP reaches its own host and nothing else does, so this gate is its own. It
// enforces the same rule as the one inside HttpFetcher - one request at a time,
// five seconds apart, plus jitter - which is what keeps a browser from being an
// exception to the politeness rules rather than a participant in them.
builder.Services.AddSingleton<HostPolitenessGate>();
builder.Services.AddSingleton(new EkapBrowserOptions
{
    AnnouncementButtonName = EkapUi.AnnouncementButtonName(configDirectory)
});
builder.Services.AddSingleton<PlaywrightEkapSearchClient>();
builder.Services.AddSingleton<IEkapSearchClient>(services =>
    services.GetRequiredService<PlaywrightEkapSearchClient>());
builder.Services.AddSingleton<ISourceAdapter, EkapAdapter>();
builder.Services.AddSingleton<ISourceAdapter, EkapDirectProcurementAdapter>();

builder.Services.AddSingleton<ISourceAdapterRegistry, SourceAdapterRegistry>();
builder.Services.AddSingleton<ParsePipeline>();
builder.Services.AddSingleton<DocumentTextExtractor>();
builder.Services.AddSingleton<DocumentDownloader>();

builder.Services.AddHostedService<CollectionWorker>();
builder.Services.AddHostedService<ParseWorker>();
builder.Services.AddHostedService<DocumentWorker>();

// What the tenders we watched ended up as. Runs on its own slow clock, behind
// the same browser and the same politeness gate as the EKAP search.
builder.Services.AddSingleton<AwardParser>();
builder.Services.AddSingleton<AwardSweeper>();
builder.Services.AddHostedService<AwardWorker>();

// Telegram is optional at startup. Without a token the rest of the system still
// collects, archives, parses and scores; it just stays quiet. Refusing to start
// would make an unfinished bot setup look like a broken worker.
var botToken = builder.Configuration["PR_TELEGRAM_BOT_TOKEN"];
var chatId = builder.Configuration["PR_TELEGRAM_CHAT_ID"];

// Health monitoring runs either way, so a half-finished bot setup does not
// also mean nobody is watching the sources.
builder.Services.AddSingleton(new HealthOptions());
builder.Services.AddSingleton<HealthMonitor>();
builder.Services.AddHostedService<HealthWorker>();

if (!string.IsNullOrWhiteSpace(botToken) && !string.IsNullOrWhiteSpace(chatId))
{
    builder.Services.AddSingleton(new TelegramOptions { BotToken = botToken, ChatId = chatId });
    builder.Services.AddSingleton<IAlertChannel, TelegramAlertChannel>();
    builder.Services.AddSingleton(new NotificationOptions(liveKeywords));
    builder.Services.AddSingleton<TelegramClient>();
    builder.Services.AddSingleton<NoticeMessageFormatter>();
    builder.Services.AddSingleton<NotificationService>();
    builder.Services.AddSingleton<CallbackProcessor>();
    builder.Services.AddSingleton<EkapAnnouncementEnricher>();
    builder.Services.AddSingleton<ICommandQueries, CommandQueries>();
    builder.Services.AddSingleton<CommandResponder>();

    builder.Services.AddHostedService<NotificationWorker>();
    builder.Services.AddHostedService<CallbackWorker>();
}
else
{
    builder.Services.AddSingleton<IAlertChannel, LoggingAlertChannel>();
}

using var host = builder.Build();

// Startup notes go through the host's logger, not Serilog's static Log. Nothing
// configures the static one here, so everything written to it before this point
// went nowhere - including the warning about a missing Telegram token, which is
// precisely the message somebody setting this up needs to see.
var startup = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

if (envFile is not null)
{
    // When the worker starts from a scheduled task or a service rather than from
    // a shell, which file it read is the first thing you want to know if a
    // setting is not what you expected.
    startup.LogInformation("Read environment from {Path}", envFile);
}

// And which config, for a better reason still. A deployed copy that stopped
// being updated is invisible from the repository: the file there looks right,
// the running worker simply never sees it. One line turns that from an
// afternoon into a grep.
startup.LogInformation("Read configuration from {Path}", configDirectory);

if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
{
    startup.LogWarning(
        "PR_TELEGRAM_BOT_TOKEN or PR_TELEGRAM_CHAT_ID is not set; notifications are off. "
        + "Notices are still collected, parsed and scored, and health alarms go to the log. "
        + "See START-HERE.md step 3.");
}

try
{
    host.Run();
    return 0;
}
catch (Exception exception)
{
    Log.Fatal(exception, "Worker terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

static string Required(IConfiguration configuration, string key)
    => configuration[key] is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException(
            $"{key} is not set. See docs/decisions.md D-06.");

string ReadConfig(string fileName) => ConfigDirectory.Read(configDirectory, fileName);
