using System.Text;
using ProcurementRadar.Api;
using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Enums;
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

app.MapGet("/api/sources/troubled", async (ICommandQueries queries, CancellationToken cancellationToken)
    => Results.Ok(await queries.ListTroubledSourcesAsync(30, cancellationToken).ConfigureAwait(false)));

// --- what tenders went for ---------------------------------------------------

app.MapGet("/api/awards", async (int? limit, IAwardRepository awards, CancellationToken cancellationToken)
    => Results.Ok(await awards
        .ListRecentAsync(Math.Clamp(limit ?? 50, 1, 200), cancellationToken)
        .ConfigureAwait(false)));

// --- settings ----------------------------------------------------------------

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

internal sealed record SettingChange(string Value, string? UpdatedBy);

/// <summary>Named so the tests can reach the host builder.</summary>
public partial class Program;
