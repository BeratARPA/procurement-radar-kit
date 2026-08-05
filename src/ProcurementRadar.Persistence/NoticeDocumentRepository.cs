using Dapper;
using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Repositories;

namespace ProcurementRadar.Persistence;

public sealed class NoticeDocumentRepository : INoticeDocumentRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public NoticeDocumentRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task RecordUrlAsync(long noticeId, string url, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(
                NoticeDocumentSql.RecordUrl,
                new { NoticeId = noticeId, Url = url },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<NoticeDocument>> ListPendingAsync(
        int maximumAttempts,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await connection.QueryAsync<NoticeDocument>(
            new CommandDefinition(
                NoticeDocumentSql.ListPending,
                new { MaximumAttempts = maximumAttempts, Limit = limit },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    public async Task MarkDownloadedAsync(NoticeDocument document, CancellationToken cancellationToken)
    {
        var parameters = new
        {
            document.Id,
            document.FileName,
            document.MimeType,
            document.SizeBytes,
            document.StoragePath,
            DownloadedAt = document.DownloadedAt?.ToUniversalTime(),
            document.ExtractedText
        };

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(
                NoticeDocumentSql.MarkDownloaded,
                parameters,
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task RecordFailureAsync(long documentId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(
                NoticeDocumentSql.RecordFailure,
                new { DocumentId = documentId },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }
}
