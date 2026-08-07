using Dapper;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Repositories;

namespace ProcurementRadar.Persistence;

public sealed class AwardRepository : IAwardRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly TimeProvider _timeProvider;

    public AwardRepository(IDbConnectionFactory connectionFactory, TimeProvider timeProvider)
    {
        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<AwardCandidate>> ListPendingAsync(
        int ripeDays,
        int maximumAttempts,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<AwardCandidate>(
            new CommandDefinition(
                AwardSql.ListPending,
                new { RipeDays = ripeDays, MaximumAttempts = maximumAttempts, Limit = limit },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    public async Task RecordAsync(long noticeId, Award? award, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(
                AwardSql.Record,
                new
                {
                    NoticeId = noticeId,
                    WinnerName = award?.WinnerName,
                    ContractAmount = award?.ContractAmount,
                    BidCount = award?.BidCount,
                    AwardedAt = award?.AwardedAt,
                    CheckedAt = _timeProvider.GetUtcNow()
                },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AwardSummary>> ListRecentAsync(int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<AwardSummary>(
            new CommandDefinition(AwardSql.ListRecent, new { Limit = limit }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }
}
