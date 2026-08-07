using System.Text;
using ProcurementRadar.Api;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Notifications;
using ProcurementRadar.Domain.Repositories;
using ProcurementRadar.Domain.Normalization;
using ProcurementRadar.Domain.Settings;
using ProcurementRadar.Scoring;
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

// Not a login. One token, one team (D-16): it keeps another device on the same
// home network from changing a setting by accident, and it is the only thing
// standing between the api and anything that can reach port 5080.
var apiToken = builder.Configuration["PR_API_TOKEN"];

PersistenceSetup.RegisterTypeHandlers();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IDbConnectionFactory>(_ => new NpgsqlConnectionFactory(connectionString));
builder.Services.AddSingleton<ISourceRepository, SourceRepository>();
builder.Services.AddSingleton<INoticeRepository, NoticeRepository>();
builder.Services.AddSingleton<IAwardRepository, AwardRepository>();
builder.Services.AddSingleton<ISettingRepository, SettingRepository>();
builder.Services.AddSingleton<ICommandQueries, CommandQueries>();

// The preview scores notices itself, so it needs the same normalizer the worker
// scores with - the one that folds Turkish letters and strips the suffixes. Read
// from the setting table, because that is where it lives now (D-16); the file is
// only what an empty table is seeded from.
builder.Services.AddSingleton(services =>
{
    var settings = services.GetRequiredService<ISettingRepository>();
    var stored = settings.GetAsync(SettingKeys.Normalization, CancellationToken.None).GetAwaiter().GetResult();

    return new NoticeNormalizer(NormalizationRules.Load(
        stored?.Value ?? ConfigDirectory.Read(ConfigDirectory.Find(), "tr-normalization.json")));
});

// The interface is served from a different origin while it is being developed
// (flutter run puts it on its own port), and from the same machine either way.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseCors();

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

app.Use(async (context, next) =>
{
    if (!string.IsNullOrWhiteSpace(apiToken)
        && !string.Equals(context.Request.Headers["X-Api-Token"], apiToken, StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        return;
    }

    await next().ConfigureAwait(false);
});

// --- what the system is doing ------------------------------------------------

app.MapGet("/api/health", async (ICommandQueries queries, CancellationToken cancellationToken)
    => Results.Ok(await queries.GetStatusAsync(cancellationToken).ConfigureAwait(false)));

// --- notices -----------------------------------------------------------------

app.MapGet("/api/notices", async (
    string? q,
    int? minScore,
    int? limit,
    ICommandQueries queries,
    CancellationToken cancellationToken) =>
{
    var take = Math.Clamp(limit ?? 50, 1, 200);

    // A term searches the whole archive; without one the answer is what is still
    // open, which is the question the list screen opens on.
    var notices = string.IsNullOrWhiteSpace(q)
        ? await queries.ListOpenAsync(minScore ?? 0, take, cancellationToken).ConfigureAwait(false)
        : await queries.SearchAsync(q, take, cancellationToken).ConfigureAwait(false);

    return Results.Ok(notices);
});

app.MapGet("/api/notices/{ikn}", async (
    string ikn,
    ICommandQueries queries,
    CancellationToken cancellationToken) =>
{
    var detail = await queries.GetDetailAsync(ikn, cancellationToken).ConfigureAwait(false);

    return detail is null ? Results.NotFound() : Results.Ok(detail);
});

app.MapGet("/api/notices/closing", async (
    int? withinHours,
    ICommandQueries queries,
    CancellationToken cancellationToken)
    => Results.Ok(await queries
        .ListClosingSoonAsync(Math.Clamp(withinHours ?? 48, 1, 720), 100, cancellationToken)
        .ConfigureAwait(false)));

app.MapPost("/api/notices/{id:long}/status", async (
    long id,
    StatusChange change,
    INoticeRepository notices,
    CancellationToken cancellationToken) =>
{
    // The interface sends what the schema stores - not_relevant, bid_submitted -
    // and the enum spells the same thing without the underscore.
    if (!Enum.TryParse<NoticeStatus>(change.Status.Replace("_", string.Empty, StringComparison.Ordinal),
            ignoreCase: true,
            out var status))
    {
        return Results.BadRequest(new { problem = $"'{change.Status}' is not a status." });
    }

    await notices.UpdateStatusAsync(id, status, cancellationToken).ConfigureAwait(false);

    return Results.NoContent();
});

// --- sources -----------------------------------------------------------------

app.MapGet("/api/sources", async (ISourceRepository sources, CancellationToken cancellationToken)
    => Results.Ok(await sources.ListActiveAsync(cancellationToken).ConfigureAwait(false)));

// Every source, switched-off ones included, because the reason a source is off
// is exactly what somebody opening this screen wants to read.
app.MapGet("/api/sources/all", async (ISourceRepository sources, CancellationToken cancellationToken)
    => Results.Ok(await sources.ListAllAsync(cancellationToken).ConfigureAwait(false)));

app.MapGet("/api/sources/{id:long}", async (
    long id,
    ISourceRepository sources,
    CancellationToken cancellationToken) =>
{
    var source = await sources.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

    return source is null ? Results.NotFound() : Results.Ok(source);
});

app.MapPost("/api/sources/{id:long}/active", async (
    long id,
    ActiveChange change,
    ISourceRepository sources,
    ILoggerFactory loggers,
    CancellationToken cancellationToken) =>
{
    if (change.IsActive)
    {
        await sources.ActivateAsync(id, cancellationToken).ConfigureAwait(false);
    }
    else
    {
        await sources.DeactivateAsync(id, cancellationToken).ConfigureAwait(false);
    }

    loggers.CreateLogger("Sources").LogInformation(
        "Source {SourceId} was switched {State} from the interface",
        id,
        change.IsActive ? "on" : "off");

    return Results.NoContent();
});

app.MapGet("/api/sources/troubled", async (ICommandQueries queries, CancellationToken cancellationToken)
    => Results.Ok(await queries.ListTroubledSourcesAsync(30, cancellationToken).ConfigureAwait(false)));

// --- what tenders went for ---------------------------------------------------

app.MapGet("/api/awards", async (int? limit, IAwardRepository awards, CancellationToken cancellationToken)
    => Results.Ok(await awards
        .ListRecentAsync(Math.Clamp(limit ?? 50, 1, 200), cancellationToken)
        .ConfigureAwait(false)));

// --- settings ----------------------------------------------------------------

// --- what a filter would have caught ------------------------------------------

app.MapPost("/api/filter/preview", async (
    FilterPreview request,
    ICommandQueries queries,
    NoticeNormalizer normalizer,
    CancellationToken cancellationToken) =>
{
    if (SettingValidator.Validate(SettingKeys.Keywords, request.Keywords) is { } problem)
    {
        return Results.BadRequest(new { problem });
    }

    var rules = KeywordRules.Load(request.Keywords);
    var threshold = request.Threshold ?? rules.NotifyThreshold;

    // Built here and thrown away. Nothing is written and nothing the worker
    // holds is touched: a preview that changed what the worker is doing would be
    // the opposite of a preview.
    var scorer = new RuleScorer(new Fixed<KeywordRules>(rules), normalizer);

    var notices = await queries
        .ListForPreviewAsync(Math.Clamp(request.Limit ?? 1000, 1, 5000), cancellationToken)
        .ConfigureAwait(false);

    var scored = notices
        .Select(notice => new
        {
            notice.NoticeId,
            notice.Title,
            notice.AuthorityName,
            Score = scorer.Score(notice.Title, notice.Description, notice.OkasCodes).Value
        })
        .ToList();

    var passing = scored.Where(row => row.Score >= threshold).OrderByDescending(row => row.Score).ToList();

    return Results.Ok(new
    {
        considered = scored.Count,
        wouldNotify = passing.Count,
        threshold,
        // Enough to recognise the answer, not so much that the response is the
        // archive again. What was missed matters as much as what was caught.
        caught = passing.Take(25),
        missed = scored
            .Where(row => row.Score > 0 && row.Score < threshold)
            .OrderByDescending(row => row.Score)
            .Take(25)
    });
});

app.MapGet("/api/settings", async (ISettingRepository settings, CancellationToken cancellationToken)
    => Results.Ok(await settings.ListAsync(cancellationToken).ConfigureAwait(false)));

app.MapGet("/api/settings/{key}", async (
    string key,
    ISettingRepository settings,
    CancellationToken cancellationToken) =>
{
    var setting = await settings.GetAsync(key, cancellationToken).ConfigureAwait(false);

    return setting is null ? Results.NotFound() : Results.Ok(setting);
});

app.MapPut("/api/settings/{key}", async (
    string key,
    SettingChange change,
    ISettingRepository settings,
    ILoggerFactory loggers,
    CancellationToken cancellationToken) =>
{
    if (SettingValidator.Validate(key, change.Value) is { } problem)
    {
        // Refused before it is stored. The worker would survive it - it keeps the
        // previous value and logs an error - but the person who pressed save
        // would never hear about it.
        return Results.BadRequest(new { problem });
    }

    await settings.SaveAsync(key, change.Value, change.UpdatedBy, cancellationToken).ConfigureAwait(false);

    loggers.CreateLogger("Settings").LogInformation("Setting {Key} was changed by {By}", key, change.UpdatedBy);

    return Results.NoContent();
});

app.MapGet("/api/settings/{key}/history", async (
    string key,
    int? limit,
    ISettingRepository settings,
    CancellationToken cancellationToken)
    => Results.Ok(await settings
        .ListHistoryAsync(key, Math.Clamp(limit ?? 20, 1, 100), cancellationToken)
        .ConfigureAwait(false)));

startup.LogInformation("Api listening on {Urls}", string.Join(", ", app.Urls.DefaultIfEmpty("(configured)")));

app.Run();

internal sealed record StatusChange(string Status);

internal sealed record FilterPreview(string Keywords, int? Threshold, int? Limit);

internal sealed record ActiveChange(bool IsActive);

internal sealed record SettingChange(string Value, string? UpdatedBy);

/// <summary>Named so the tests can reach the host builder.</summary>
public partial class Program;
