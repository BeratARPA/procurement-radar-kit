using Dapper;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Repositories;

namespace ProcurementRadar.Persistence;

public sealed class NoticeScoreRepository : INoticeScoreRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly TimeProvider _timeProvider;

    public NoticeScoreRepository(IDbConnectionFactory connectionFactory, TimeProvider timeProvider)
    {
        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider;
    }

    public async Task UpsertAsync(NoticeScore score, CancellationToken cancellationToken)
    {
        var parameters = new
        {
            score.NoticeId,
            score.RuleScore,
            score.Rationale,
            score.EstimatedManDays,
            score.RiskNote,
            score.Model,
            ScoredAt = score.ScoredAt ?? _timeProvider.GetUtcNow()
        };

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(NoticeScoreSql.Upsert, parameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }
}
