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

        // Built from the notice itself rather than listed by hand, then
        // overridden where the repository knows better. The hand-written list
        // this replaces was one property behind the schema for two hours:
        // bid_deadline_time_assumed was added to the table and to the statement
        // and nobody added it here, and PostgreSQL does not report a missing
        // parameter - @ is its absolute-value operator, so it reported a missing
        // column for a column that existed. Every parse run failed until
        // somebody read the log.
        //
        // Taking the properties off the entity means the next column added to
        // the notice cannot go missing on this path at all.
        var parameters = new DynamicParameters(notice);

        // Deadlines arrive from Parse() already converted out of Europe/Istanbul,
        // but normalize anyway: a non-zero offset is a hard error on a
        // timestamptz parameter and this is the field we cannot afford to drop.
        parameters.Add("BidDeadlineAt", notice.BidDeadlineAt?.ToUniversalTime());
        parameters.Add("OpeningAt", notice.OpeningAt?.ToUniversalTime());

        // Fills both first_seen_at and last_seen_at on insert, and only
        // last_seen_at on conflict. The caller's values are ignored by design.
        parameters.Add("SeenAt", seenAt);

        // Text, not the enum: see the note in SourceRepository.UpsertAsync.
        parameters.Add("Status", EnumText.ToText(notice.Status));

        // Same conversion, and it only started mattering when something began
        // setting it. The column is text and Dapper would have sent the enum as
        // an integer: '0' where 'direct_procurement' belongs, on a column
        // nothing constrains.
        parameters.Add(
            "ProcurementType",
            notice.ProcurementType is { } type ? EnumText.ToText(type) : null);

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        return await connection.QuerySingleAsync<NoticeUpsertResult>(
            new CommandDefinition(NoticeSql.Upsert, parameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<bool> FillProcedureAsync(
        long noticeId,
        ProcurementType? procurementType,
        string? procedureCode,
        CancellationToken cancellationToken)
    {
        if (procurementType is null && procedureCode is null)
        {
            // Nothing to say. Most sources are in this position on every row and
            // a statement per row that can only be a no-op is a statement not
            // worth sending.
            return false;
        }

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                NoticeSql.FillProcedure,
                new
                {
                    NoticeId = noticeId,
                    ProcurementType = procurementType is { } type ? EnumText.ToText(type) : null,
                    ProcedureCode = procedureCode
                },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return affected > 0;
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
