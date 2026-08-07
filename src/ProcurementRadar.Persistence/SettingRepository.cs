using Dapper;
using ProcurementRadar.Domain.Settings;

namespace ProcurementRadar.Persistence;

public sealed class SettingRepository : ISettingRepository
{
    private const string SeededBy = "seed";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly TimeProvider _timeProvider;

    public SettingRepository(IDbConnectionFactory connectionFactory, TimeProvider timeProvider)
    {
        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<Setting>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<Setting>(
            new CommandDefinition(SettingSql.List, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    public async Task<Setting?> GetAsync(string key, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        return await connection.QuerySingleOrDefaultAsync<Setting>(
            new CommandDefinition(SettingSql.Get, new { Key = key }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<DateTimeOffset?> GetVersionAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        // Read as DateTime and converted here, not asked for as a DateTimeOffset.
        // Npgsql hands a timestamptz back as a DateTime with Kind Utc, and
        // Dapper's scalar path converts with System.Convert, which has no cast
        // between the two: asking for the offset type throws at run time while
        // compiling perfectly and passing every test with a fake repository
        // behind it. The same trap took a day over the notice records; this is
        // the scalar version of it.
        var version = await connection.ExecuteScalarAsync<DateTime?>(
            new CommandDefinition(SettingSql.Version, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return version is { } value ? new DateTimeOffset(value.ToUniversalTime()) : null;
    }

    public async Task SaveAsync(string key, string json, string? updatedBy, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        // One transaction: the history entry and the new value are the same
        // change, and a crash between them would lose the only copy of what the
        // setting used to be.
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(
                SettingSql.ArchiveCurrent,
                new { Key = key, Now = now },
                transaction,
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(
                SettingSql.Save,
                new { Key = key, Value = json, Now = now, UpdatedBy = updatedBy },
                transaction,
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> InsertIfMissingAsync(string key, string json, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var inserted = await connection.ExecuteAsync(
            new CommandDefinition(
                SettingSql.InsertIfMissing,
                new
                {
                    Key = key,
                    Value = json,
                    Now = _timeProvider.GetUtcNow(),
                    UpdatedBy = SeededBy
                },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return inserted > 0;
    }

    public async Task<IReadOnlyList<Setting>> ListHistoryAsync(
        string key,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<Setting>(
            new CommandDefinition(
                SettingSql.ListHistory,
                new { Key = key, Limit = limit },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }
}
