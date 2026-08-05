using System.Net;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Adapters;
using ProcurementRadar.Domain.Fetching;
using ProcurementRadar.Domain.Normalization;
using ProcurementRadar.Domain.Parsing;
using ProcurementRadar.Domain.Repositories;
using ProcurementRadar.Domain.Notifications;
using ProcurementRadar.Domain.Scoring;
using ProcurementRadar.Parsers;
using ProcurementRadar.Notifier;
using ProcurementRadar.Persistence;
using ProcurementRadar.Scoring;
using ProcurementRadar.Worker;
using Serilog;

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
// touch the disk.
var datePatterns = TurkishDatePatterns.Load(ReadConfig("tr-date-patterns.json"));
var normalization = NormalizationRules.Load(ReadConfig("tr-normalization.json"));
var personalData = PersonalDataRules.Load(ReadConfig("tr-personal-data.json"));
var keywords = KeywordRules.Load(ReadConfig("keywords.json"));
var messages = MessageTemplates.Load(ReadConfig("tr-messages.json"));

PersistenceSetup.RegisterTypeHandlers();

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton(new HttpFetcherOptions { ContactEmail = contactEmail });
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

builder.Services.AddSingleton(datePatterns);
builder.Services.AddSingleton<ITurkishDateParser, TurkishDateParser>();
builder.Services.AddSingleton(new NoticeNormalizer(normalization));
builder.Services.AddSingleton<ExternalKeyBuilder>();
builder.Services.AddSingleton(new PersonalDataMasker(personalData));
builder.Services.AddSingleton(keywords);
builder.Services.AddSingleton<INoticeScorer, RuleScorer>();

builder.Services.AddSingleton<ISourceAdapter, GenericTableAdapter>();
builder.Services.AddSingleton<ISourceAdapterRegistry, SourceAdapterRegistry>();
builder.Services.AddSingleton<ParsePipeline>();
builder.Services.AddSingleton<DocumentTextExtractor>();
builder.Services.AddSingleton<DocumentDownloader>();

builder.Services.AddHostedService<CollectionWorker>();
builder.Services.AddHostedService<ParseWorker>();
builder.Services.AddHostedService<DocumentWorker>();

// Telegram is optional at startup. Without a token the rest of the system still
// collects, archives, parses and scores; it just stays quiet. Refusing to start
// would make an unfinished bot setup look like a broken worker.
var botToken = builder.Configuration["PR_TELEGRAM_BOT_TOKEN"];
var chatId = builder.Configuration["PR_TELEGRAM_CHAT_ID"];

if (!string.IsNullOrWhiteSpace(botToken) && !string.IsNullOrWhiteSpace(chatId))
{
    builder.Services.AddSingleton(new TelegramOptions { BotToken = botToken, ChatId = chatId });
    builder.Services.AddSingleton(messages);
    builder.Services.AddSingleton(new NotificationOptions { MinimumScore = keywords.NotifyThreshold });
    builder.Services.AddSingleton<TelegramClient>();
    builder.Services.AddSingleton<NoticeMessageFormatter>();
    builder.Services.AddSingleton<NotificationService>();
    builder.Services.AddSingleton<CallbackProcessor>();

    builder.Services.AddHostedService<NotificationWorker>();
    builder.Services.AddHostedService<CallbackWorker>();
}
else
{
    Log.Warning(
        "PR_TELEGRAM_BOT_TOKEN or PR_TELEGRAM_CHAT_ID is not set; notifications are off. "
        + "Notices are still collected, parsed and scored. See START-HERE.md step 3.");
}

using var host = builder.Build();

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

static string ReadConfig(string fileName)
{
    var path = Path.GetFullPath(Path.Combine("config", fileName));

    return File.Exists(path)
        ? File.ReadAllText(path)
        : throw new FileNotFoundException(
            $"Config file not found at {path}. The worker's working directory has to be the "
            + "repository root, or the deployment root that contains config/.",
            path);
}
