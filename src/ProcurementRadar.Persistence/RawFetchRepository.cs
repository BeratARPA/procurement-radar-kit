using Dapper;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Repositories;

namespace ProcurementRadar.Persistence;

public sealed class RawFetchRepository : IRawFetchRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public RawFetchRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long> ReserveIdAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        return await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(RawFetchSql.ReserveId, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task InsertAsync(RawFetch rawFetch, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rawFetch.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawFetch.BodyPath);

        var parameters = new
        {
            rawFetch.Id,
            rawFetch.SourceId,
            // Normalized rather than trusted: PostgreSQL rejects a timestamptz
            // parameter that does not carry a zero offset, and a caller handing
            // over an Europe/Istanbul value should not blow up the archive write.
            FetchedAt = rawFetch.FetchedAt.ToUniversalTime(),
            rawFetch.HttpStatus,
            rawFetch.ETag,
            rawFetch.LastModified,
            rawFetch.ContentType,
            rawFetch.ContentHash,
            rawFetch.BodyPath,
            rawFetch.ParserVersion,
            rawFetch.IsProcessed
        };

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(RawFetchSql.Insert, parameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<RawFetch?> GetLatestAsync(long sourceId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        return await connection.QuerySingleOrDefaultAsync<RawFetch>(
            new CommandDefinition(
                RawFetchSql.GetLatest,
                new { SourceId = sourceId },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RawFetch>> ListUnprocessedAsync(
        long? sourceId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<RawFetch>(
            new CommandDefinition(
                RawFetchSql.ListUnprocessed,
                new { SourceId = sourceId, Limit = limit },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    public async Task MarkProcessedAsync(
        long rawFetchId,
        string parserVersion,
        int noticeCount,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parserVersion);
        ArgumentOutOfRangeException.ThrowIfNegative(noticeCount);

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(
                RawFetchSql.MarkProcessed,
                new { RawFetchId = rawFetchId, ParserVersion = parserVersion, NoticeCount = noticeCount },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }
}
