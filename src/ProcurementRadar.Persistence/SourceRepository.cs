using Dapper;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Repositories;

namespace ProcurementRadar.Persistence;

public sealed class SourceRepository : ISourceRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly TimeProvider _timeProvider;

    public SourceRepository(IDbConnectionFactory connectionFactory, TimeProvider timeProvider)
    {
        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<Source>> ListActiveAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<Source>(
            new CommandDefinition(SourceSql.ListActive, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    public Task RecordFetchSuccessAsync(long sourceId, bool foundNewNotice, CancellationToken cancellationToken)
        => ExecuteAsync(
            SourceSql.RecordFetchSuccess,
            new { SourceId = sourceId, Now = _timeProvider.GetUtcNow(), FoundNewNotice = foundNewNotice },
            cancellationToken);

    public Task RecordFetchFailureAsync(long sourceId, CancellationToken cancellationToken)
        => ExecuteAsync(
            SourceSql.RecordFetchFailure,
            new { SourceId = sourceId, Now = _timeProvider.GetUtcNow() },
            cancellationToken);

    public Task RecordRobotsCheckAsync(long sourceId, CancellationToken cancellationToken)
        => ExecuteAsync(
            SourceSql.RecordRobotsCheck,
            new { SourceId = sourceId, Now = _timeProvider.GetUtcNow() },
            cancellationToken);

    public Task DeactivateAsync(long sourceId, CancellationToken cancellationToken)
        => ExecuteAsync(SourceSql.Deactivate, new { SourceId = sourceId }, cancellationToken);

    private async Task ExecuteAsync(string sql, object parameters, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }
}
