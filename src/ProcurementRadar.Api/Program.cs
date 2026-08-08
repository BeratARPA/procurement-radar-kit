using System.Reflection;
using System.Text;
using Microsoft.OpenApi.Models;
using ProcurementRadar.Api;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Normalization;
using ProcurementRadar.Domain.Notifications;
using ProcurementRadar.Domain.Repositories;
using ProcurementRadar.Domain.Settings;
using ProcurementRadar.Persistence;
using Serilog;

// Same reason as the worker: the console defaults to the system code page on
// Windows, and a Turkish title in a log line comes out as question marks.
Console.OutputEncoding = Encoding.UTF8;

DotEnv.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services));

var connectionString = builder.Configuration["PR_DB_CONNECTION_STRING"]
    ?? throw new InvalidOperationException("PR_DB_CONNECTION_STRING is not set. See docs/decisions.md D-06.");

var apiToken = builder.Configuration["PR_API_TOKEN"];

PersistenceSetup.RegisterTypeHandlers();

// --- controllers, and the document that describes them -----------------------

builder.Services.AddControllers(options => options.Filters.Add<ApiKeyFilter>());

// ProblemDetails for what the framework rejects too, so a malformed body and a
// refused setting come back in the same shape.
builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Off by default, and the default is a lie. Without this every string in the
    // document is nullable - including a title the C# declares non-nullable and
    // the database stores NOT NULL - and the Dart client generated from that
    // document (D-16) makes every screen unpack an Optional that cannot be empty.
    options.SupportNonNullableReferenceTypes();

    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Procurement Radar",
        Version = "v1",
        Description =
            "The local api behind the interface (D-16). Runs on the machine that "
            + "collects, reachable on the home network, and every request wants the "
            + "X-Api-Token header."
    });

    // The xml comments on the controllers are the documentation. No second copy
    // of it exists to fall out of date.
    var comments = Path.Combine(
        AppContext.BaseDirectory,
        $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");

    if (File.Exists(comments))
    {
        options.IncludeXmlComments(comments);
    }

    options.AddSecurityDefinition(ApiKeyFilter.HeaderName, new OpenApiSecurityScheme
    {
        Name = ApiKeyFilter.HeaderName,
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Description = "The value of PR_API_TOKEN. One token for one team, not a login."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = ApiKeyFilter.HeaderName
            }
        }] = []
    });
});

// --- the same repositories the worker uses -----------------------------------

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IDbConnectionFactory>(_ => new NpgsqlConnectionFactory(connectionString));
builder.Services.AddSingleton<ISourceRepository, SourceRepository>();
builder.Services.AddSingleton<INoticeRepository, NoticeRepository>();
builder.Services.AddSingleton<IAwardRepository, AwardRepository>();
builder.Services.AddSingleton<ISettingRepository, SettingRepository>();
builder.Services.AddSingleton<ICommandQueries, CommandQueries>();
builder.Services.AddSingleton<IRawFetchRepository, RawFetchRepository>();

// Trying a selector reads the page the collector already got (D-04) rather than
// fetching it again, so the source screen costs the authority nothing.
builder.Services.AddSingleton(new StoragePathBuilder(
    builder.Configuration[StoragePathBuilder.DataDirectoryVariable]
    ?? StoragePathBuilder.DefaultDataDirectory));
builder.Services.AddSingleton<IRawArchive, FileSystemRawArchive>();
builder.Services.AddSingleton<SourceChecker>();

// The filter preview scores notices itself, so it needs the same normalizer the
// worker scores with. Read from the setting table, because that is where it
// lives now (D-16); the file is only what an empty table is seeded from.
builder.Services.AddSingleton(services =>
{
    var settings = services.GetRequiredService<ISettingRepository>();
    var stored = settings.GetAsync(SettingKeys.Normalization, CancellationToken.None).GetAwaiter().GetResult();

    return new NoticeNormalizer(NormalizationRules.Load(
        stored?.Value ?? ConfigDirectory.Read(ConfigDirectory.Find(), "tr-normalization.json")));
});

// The interface is served from its own port while it is being developed, and
// from this machine either way.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

// Left on outside development on purpose. This api has one reader on one home
// network, and being able to see and try the endpoints from a phone is why it
// is here rather than a debugging convenience.
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Procurement Radar v1");
    options.DocumentTitle = "Procurement Radar";
});

app.MapControllers();

var startup = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

if (string.IsNullOrWhiteSpace(apiToken))
{
    // Refusing to start would be worse: the api would be unreachable and the
    // reason invisible. Saying it once, loudly, at every start is the honest
    // middle.
    startup.LogWarning(
        "PR_API_TOKEN is not set. Every request is accepted. Set it in .env before "
        + "this machine is reachable from anywhere but the desk it is on.");
}

startup.LogInformation("Api started. The endpoints and their shapes are at /swagger.");

app.Run();

/// <summary>Named so tests can reach the host builder.</summary>
public partial class Program;
