using Dapper;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Health;

namespace ProcurementRadar.Persistence;

public sealed class HealthRepository : IHealthRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly TimeProvider _timeProvider;

    public HealthRepository(IDbConnectionFactory connectionFactory, TimeProvider timeProvider)
    {
        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider;
    }

    public Task<IReadOnlyList<UnhealthySource>> ListFailingAsync(
        int minimumFailures,
        CancellationToken cancellationToken)
        => QueryAsync<UnhealthySource>(
            HealthSql.ListFailing,
            new { MinimumFailures = minimumFailures },
            cancellationToken);

    public Task<IReadOnlyList<BrokenParser>> ListBrokenParsersAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken)
        => QueryAsync<BrokenParser>(
            HealthSql.ListBrokenParsers,
            new { Since = since.ToUniversalTime() },
            cancellationToken);

    public Task<IReadOnlyList<SilentSource>> ListSilentAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken)
        => QueryAsync<SilentSource>(
            HealthSql.ListSilent,
            new { OlderThan = olderThan.ToUniversalTime() },
            cancellationToken);

    public Task<IReadOnlyList<NeverProducedSource>> ListNeverProducedAsync(
        DateTimeOffset fetchedSince,
        CancellationToken cancellationToken)
        => QueryAsync<NeverProducedSource>(
            HealthSql.ListNeverProduced,
            new { FetchedSince = fetchedSince.ToUniversalTime() },
            cancellationToken);

    public async Task<int> CountNoticesSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                HealthSql.CountNoticesSince,
                new { Since = since.ToUniversalTime() },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<int> CountFetchesSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                HealthSql.CountFetchesSince,
                new { Since = since.ToUniversalTime() },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<int> CountUnparsedSinceAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                HealthSql.CountUnparsed,
                new { OlderThan = olderThan.ToUniversalTime() },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<WeeklySummary> SummariseAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        return await connection.QuerySingleAsync<WeeklySummary>(
            new CommandDefinition(
                HealthSql.Summarise,
                new { Since = since.ToUniversalTime() },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<bool> TryRaiseAsync(
        HealthAlertKind kind,
        long? sourceId,
        TimeSpan cooldown,
        string? detail,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        var parameters = new
        {
            Kind = EnumText.ToText(kind),
            SourceId = sourceId,
            RaisedAt = now,
            Since = now - cooldown,
            Detail = detail
        };

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var inserted = await connection.ExecuteAsync(
            new CommandDefinition(HealthSql.TryRaise, parameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return inserted > 0;
    }

    private async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        object parameters,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<T>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }
}
