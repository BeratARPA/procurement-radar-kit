using Dapper;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Repositories;

namespace ProcurementRadar.Persistence;

public sealed class NoticeRepository : INoticeRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly TimeProvider _timeProvider;

    public NoticeRepository(IDbConnectionFactory connectionFactory, TimeProvider timeProvider)
    {
        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider;
    }

    public async Task<NoticeUpsertResult> UpsertAsync(
        ProcurementNotice notice,
        CancellationToken cancellationToken)
    {
        var seenAt = _timeProvider.GetUtcNow();

        var parameters = new
        {
            notice.SourceId,
            notice.RawFetchId,
            notice.ExternalKey,
            notice.Ikn,
            notice.AuthorityName,
            notice.Title,
            notice.Description,
            notice.ProcurementType,
            notice.ProcedureCode,
            notice.OkasCodes,
            notice.EstimatedCost,
            notice.Currency,
            // Deadlines arrive from Parse() already converted out of
            // Europe/Istanbul, but normalize anyway: a non-zero offset is a hard
            // error on a timestamptz parameter and this is the field we cannot
            // afford to drop.
            BidDeadlineAt = notice.BidDeadlineAt?.ToUniversalTime(),
            OpeningAt = notice.OpeningAt?.ToUniversalTime(),
            notice.SourceUrl,
            // Fills both first_seen_at and last_seen_at on insert, and only
            // last_seen_at on conflict. The caller's values are ignored by design.
            SeenAt = seenAt,
            // Text, not the enum: see the note in SourceRepository.UpsertAsync.
            Status = EnumText.ToText(notice.Status)
        };

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        return await connection.QuerySingleAsync<NoticeUpsertResult>(
            new CommandDefinition(NoticeSql.Upsert, parameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProcurementNotice>> ListByStatusAsync(
        NoticeStatus status,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<ProcurementNotice>(
            new CommandDefinition(
                NoticeSql.ListByStatus,
                new { Status = EnumText.ToText(status) },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    public async Task UpdateStatusAsync(
        long noticeId,
        NoticeStatus status,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(
                NoticeSql.UpdateStatus,
                new { NoticeId = noticeId, Status = EnumText.ToText(status) },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }
}
