using Dapper;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Notifications;

namespace ProcurementRadar.Persistence;

public sealed class NotificationRepository : INotificationRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly TimeProvider _timeProvider;

    public NotificationRepository(IDbConnectionFactory connectionFactory, TimeProvider timeProvider)
    {
        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<NoticeForNotification>> ListUnannouncedAsync(
        int minimumScore,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<NoticeForNotification>(
            new CommandDefinition(
                NotificationSql.ListUnannounced,
                new { MinimumScore = minimumScore, Limit = limit },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    public async Task<IReadOnlyList<NoticeForNotification>> ListDueRemindersAsync(
        TimeSpan window,
        int minimumScore,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var now = _timeProvider.GetUtcNow();

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<NoticeForNotification>(
            new CommandDefinition(
                NotificationSql.ListDueReminders,
                new
                {
                    Now = now,
                    Deadline = now + window,
                    MinimumScore = minimumScore,
                    Limit = limit
                },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    public async Task<bool> TryRecordAsync(
        long noticeId,
        NotificationKind kind,
        NotificationChannel channel,
        string status,
        CancellationToken cancellationToken)
    {
        var parameters = new
        {
            NoticeId = noticeId,
            // Text, not the enum: Dapper resolves an enum to its underlying
            // integer before it consults a type handler.
            Channel = EnumText.ToText(channel),
            Kind = EnumText.ToText(kind),
            SentAt = _timeProvider.GetUtcNow(),
            Status = status
        };

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var inserted = await connection.ExecuteAsync(
            new CommandDefinition(NotificationSql.TryRecord, parameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return inserted > 0;
    }

    public Task ReleaseAsync(
        long noticeId,
        NotificationKind kind,
        NotificationChannel channel,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            NotificationSql.Release,
            new
            {
                NoticeId = noticeId,
                Channel = EnumText.ToText(channel),
                Kind = EnumText.ToText(kind)
            },
            cancellationToken);

    public Task MarkSentAsync(
        long noticeId,
        NotificationKind kind,
        NotificationChannel channel,
        string status,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            NotificationSql.MarkSent,
            new
            {
                NoticeId = noticeId,
                Channel = EnumText.ToText(channel),
                Kind = EnumText.ToText(kind),
                Status = status,
                SentAt = _timeProvider.GetUtcNow()
            },
            cancellationToken);

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
