using Dapper;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Enums;
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

    public async Task<Source?> GetByIdAsync(long sourceId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        return await connection.QuerySingleOrDefaultAsync<Source>(
            new CommandDefinition(
                SourceSql.GetById,
                new { SourceId = sourceId },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<bool> UpsertAsync(Source source, CancellationToken cancellationToken)
    {
        var parameters = new
        {
            source.AuthorityName,
            // Sent as text, not as the enum. Dapper resolves an enum to its
            // underlying integer before it ever consults a type handler, so
            // passing NoticeStatus.New or AuthorityType.Municipality straight
            // through writes "0" and "1" into a text column. The handler still
            // does the reading; only the writing side needs saying out loud.
            AuthorityType = EnumText.ToText(source.AuthorityType),
            source.Province,
            source.District,
            source.Url,
            source.AdapterKey,
            source.AdapterConfig,
            source.PollIntervalMinutes,
            source.IsActive,
            source.Notes
        };

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(SourceSql.Upsert, parameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<SavedSource> SaveDefinitionAsync(Source source, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        return await connection.QuerySingleAsync<SavedSource>(
            new CommandDefinition(
                SourceSql.UpsertReturningId,
                Definition(source),
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<bool> UpdateDefinitionAsync(
        long sourceId,
        Source source,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var updated = await connection.ExecuteScalarAsync<long?>(
            new CommandDefinition(
                SourceSql.UpdateDefinition,
                Definition(source, sourceId),
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return updated is not null;
    }

    /// <summary>
    /// The columns a definition owns, written once.
    /// </summary>
    /// <remarks>
    /// The enum goes as text. Dapper resolves an enum to its underlying integer
    /// before it consults a type handler, so passing AuthorityType.Municipality
    /// straight through writes "1" into a text column - the handler does the
    /// reading, and only the writing side needs saying out loud.
    /// </remarks>
    private static object Definition(Source source, long? sourceId = null) => new
    {
        SourceId = sourceId ?? 0,
        source.AuthorityName,
        AuthorityType = EnumText.ToText(source.AuthorityType),
        source.Province,
        source.District,
        source.Url,
        source.AdapterKey,
        source.AdapterConfig,
        source.PollIntervalMinutes,
        source.IsActive,
        source.Notes
    };

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

    public Task ActivateAsync(long sourceId, CancellationToken cancellationToken)
        => ExecuteAsync(SourceSql.Activate, new { SourceId = sourceId }, cancellationToken);

    public async Task<IReadOnlyList<Source>> ListAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<Source>(
            new CommandDefinition(SourceSql.ListAll, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

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
