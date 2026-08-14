using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Repositories;

namespace ProcurementRadar.UnitTests.Archive;

/// <summary>
/// In-memory stand-ins. D-09 rules out integration tests, so the archive and
/// pipeline rules are checked against these rather than against PostgreSQL.
/// </summary>
internal sealed class FakeRawFetchRepository : IRawFetchRepository
{
    private long _nextId;

    public List<RawFetch> Rows { get; } = [];

    public List<(long RawFetchId, string ParserVersion, int NoticeCount)> Processed { get; } = [];

    public Task<long> ReserveIdAsync(CancellationToken cancellationToken)
        => Task.FromResult(Interlocked.Increment(ref _nextId));

    public Task InsertAsync(RawFetch rawFetch, CancellationToken cancellationToken)
    {
        Rows.Add(rawFetch);
        return Task.CompletedTask;
    }

    public Task<RawFetch?> GetLatestAsync(long sourceId, CancellationToken cancellationToken)
        => Task.FromResult(Rows
            .Where(row => row.SourceId == sourceId)
            .OrderByDescending(row => row.FetchedAt)
            .ThenByDescending(row => row.Id)
            .FirstOrDefault());

    public Task<IReadOnlyList<RawFetch>> ListUnprocessedAsync(
        long? sourceId,
        int limit,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<RawFetch>>(Rows
            .Where(row => !Processed.Any(done => done.RawFetchId == row.Id))
            .Where(row => sourceId is null || row.SourceId == sourceId)
            .OrderBy(row => row.FetchedAt)
            .Take(limit)
            .ToList());

    public Task MarkProcessedAsync(
        long rawFetchId,
        string parserVersion,
        int noticeCount,
        CancellationToken cancellationToken)
    {
        Processed.Add((rawFetchId, parserVersion, noticeCount));
        return Task.CompletedTask;
    }

    public Task<int> MarkSourceUnprocessedAsync(long sourceId, CancellationToken cancellationToken)
    {
        // "Processed" is what ListUnprocessedAsync filters on here, so forgetting
        // the entries is the same thing the UPDATE does.
        var ids = Rows
            .Where(row => row.SourceId == sourceId)
            .Select(row => row.Id)
            .ToHashSet();

        var queued = Processed.RemoveAll(done => ids.Contains(done.RawFetchId));

        return Task.FromResult(queued);
    }
}

internal sealed class FakeSourceRepository : ISourceRepository
{
    public FakeSourceRepository(params Source[] sources)
    {
        Sources = sources.ToList();
    }

    public List<Source> Sources { get; }

    public int SuccessCount { get; private set; }

    public int FailureCount { get; private set; }

    public int NewNoticeCount { get; private set; }

    public Task<IReadOnlyList<Source>> ListActiveAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Source>>(Sources.Where(source => source.IsActive).ToList());

    public Task<Source?> GetByIdAsync(long sourceId, CancellationToken cancellationToken)
        => Task.FromResult(Sources.FirstOrDefault(source => source.Id == sourceId));

    public Task<bool> UpsertAsync(Source source, CancellationToken cancellationToken)
    {
        Sources.Add(source);
        return Task.FromResult(true);
    }

    public Task<SavedSource> SaveDefinitionAsync(Source source, CancellationToken cancellationToken)
    {
        Sources.Add(source);

        return Task.FromResult(new SavedSource(source.Id, WasCreated: true));
    }

    public Task<bool> UpdateDefinitionAsync(
        long sourceId,
        Source source,
        CancellationToken cancellationToken)
    {
        var index = Sources.FindIndex(stored => stored.Id == sourceId);

        if (index < 0) return Task.FromResult(false);

        // The health columns and the rotation are kept, which is the behaviour
        // the real statement has and the reason it is a separate one.
        Sources[index] = source with
        {
            Id = sourceId,
            IsActive = Sources[index].IsActive,
            ConsecutiveFailures = Sources[index].ConsecutiveFailures,
            LastNewNoticeAt = Sources[index].LastNewNoticeAt
        };

        return Task.FromResult(true);
    }

    public Task RecordFetchSuccessAsync(long sourceId, bool foundNewNotice, CancellationToken cancellationToken)
    {
        SuccessCount++;

        if (foundNewNotice)
        {
            NewNoticeCount++;
        }

        return Task.CompletedTask;
    }

    public Task RecordFetchFailureAsync(long sourceId, CancellationToken cancellationToken)
    {
        FailureCount++;
        return Task.CompletedTask;
    }

    public Task RecordRobotsCheckAsync(long sourceId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public List<(long SourceId, DeactivationReason Reason)> Deactivated { get; } = [];

    public Task DeactivateAsync(long sourceId, DeactivationReason reason, CancellationToken cancellationToken)
    {
        Deactivated.Add((sourceId, reason));

        var index = Sources.FindIndex(stored => stored.Id == sourceId);

        if (index >= 0)
        {
            Sources[index] = Sources[index] with { IsActive = false, DeactivatedReason = reason };
        }

        return Task.CompletedTask;
    }

    public List<long> Activated { get; } = [];

    public Task ActivateAsync(long sourceId, CancellationToken cancellationToken)
    {
        Activated.Add(sourceId);

        var index = Sources.FindIndex(stored => stored.Id == sourceId);

        if (index >= 0)
        {
            // The real statement clears the reason with the switch. A fake that
            // kept it would let a test pass that leaves a recovered source
            // looking quarantined.
            Sources[index] = Sources[index] with
            {
                IsActive = true,
                ConsecutiveFailures = 0,
                DeactivatedAt = null,
                DeactivatedReason = null,
                QuarantineProbedAt = null
            };
        }

        return Task.CompletedTask;
    }

    public List<long> Probed { get; } = [];

    public Task RecordQuarantineProbeAsync(long sourceId, CancellationToken cancellationToken)
    {
        Probed.Add(sourceId);
        return Task.CompletedTask;
    }

    /// <summary>Mirrors the statement: only the reasons that can stop being true.</summary>
    public Task<IReadOnlyList<Source>> ListQuarantinedAsync(int limit, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Source>>(Sources
            .Where(source => !source.IsActive
                && source.DeactivatedReason is { } reason
                && reason.CanRecover())
            .OrderBy(source => source.QuarantineProbedAt ?? DateTimeOffset.MinValue)
            .ThenBy(source => source.Id)
            .Take(limit)
            .ToList());

    public Task<IReadOnlyList<Source>> ListAllAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Source>>(Sources.ToList());
}

/// <summary>
/// Reproduces the upsert semantics the schema enforces: keyed on external_key,
/// first_seen_at written once, last_seen_at moved every time.
/// </summary>
internal sealed class FakeNoticeRepository : INoticeRepository
{
    private readonly TimeProvider _timeProvider;
    private long _nextId;

    public FakeNoticeRepository(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public List<ProcurementNotice> Notices { get; } = [];

    public Task<NoticeUpsertResult> UpsertAsync(ProcurementNotice notice, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var existing = Notices.FindIndex(stored =>
            string.Equals(stored.ExternalKey, notice.ExternalKey, StringComparison.Ordinal));

        if (existing >= 0)
        {
            Notices[existing] = Notices[existing] with { LastSeenAt = now };
            return Task.FromResult(new NoticeUpsertResult(Notices[existing].Id, false));
        }

        var id = Interlocked.Increment(ref _nextId);
        Notices.Add(notice with { Id = id, FirstSeenAt = now, LastSeenAt = now });

        return Task.FromResult(new NoticeUpsertResult(id, true));
    }

    public Task<bool> FillProcedureAsync(
        long noticeId,
        ProcurementType? procurementType,
        string? procedureCode,
        CancellationToken cancellationToken)
    {
        if (procurementType is null && procedureCode is null)
        {
            return Task.FromResult(false);
        }

        var index = Notices.FindIndex(notice => notice.Id == noticeId);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        var stored = Notices[index];

        // The same coalesce the statement does, and it has to be here rather
        // than a blind assignment: a fake that overwrites where the database
        // fills would let a test pass on behaviour the driver does not have.
        if (stored.ProcurementType is not null && stored.ProcedureCode is not null)
        {
            return Task.FromResult(false);
        }

        Notices[index] = stored with
        {
            ProcurementType = stored.ProcurementType ?? procurementType,
            ProcedureCode = stored.ProcedureCode ?? procedureCode
        };

        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<ProcurementNotice>> ListByStatusAsync(
        NoticeStatus status,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ProcurementNotice>>(
            Notices.Where(notice => notice.Status == status).ToList());

    public Task UpdateStatusAsync(long noticeId, NoticeStatus status, CancellationToken cancellationToken)
    {
        var index = Notices.FindIndex(notice => notice.Id == noticeId);
        if (index >= 0)
        {
            Notices[index] = Notices[index] with { Status = status };
        }

        return Task.CompletedTask;
    }
}

internal sealed class FakeNoticeScoreRepository : INoticeScoreRepository
{
    public List<NoticeScore> Scores { get; } = [];

    public Task UpsertAsync(NoticeScore score, CancellationToken cancellationToken)
    {
        Scores.RemoveAll(existing => existing.NoticeId == score.NoticeId);
        Scores.Add(score);

        return Task.CompletedTask;
    }
}

internal sealed class FakeNoticeDocumentRepository : INoticeDocumentRepository
{
    private long _nextId;

    public List<NoticeDocument> Documents { get; } = [];

    public Task RecordUrlAsync(long noticeId, string url, CancellationToken cancellationToken)
    {
        // Mirrors UNIQUE (notice_id, url): a repeat is ignored, not duplicated.
        if (!Documents.Any(d => d.NoticeId == noticeId && d.Url == url))
        {
            Documents.Add(new NoticeDocument
            {
                Id = Interlocked.Increment(ref _nextId),
                NoticeId = noticeId,
                Url = url
            });
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NoticeDocument>> ListPendingAsync(
        int maximumAttempts,
        int limit,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<NoticeDocument>>(Documents
            .Where(d => d.DownloadedAt is null
                && d.UnavailableReason is null
                && d.FailedAttempts < maximumAttempts)
            .OrderBy(d => d.Id)
            .Take(limit)
            .ToList());

    public Task MarkDownloadedAsync(NoticeDocument document, CancellationToken cancellationToken)
    {
        var index = Documents.FindIndex(d => d.Id == document.Id);
        if (index >= 0)
        {
            Documents[index] = document;
        }

        return Task.CompletedTask;
    }

    public Task RecordFailureAsync(long documentId, CancellationToken cancellationToken)
    {
        var index = Documents.FindIndex(d => d.Id == documentId);
        if (index >= 0)
        {
            Documents[index] = Documents[index] with { FailedAttempts = Documents[index].FailedAttempts + 1 };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(long NoticeId, string Ikn)>> ListMissingAnnouncementsAsync(
        int minimumScore,
        int limit,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<(long, string)>>([]);

    public Task StoreAnnouncementAsync(
        long noticeId,
        string url,
        string fileName,
        string text,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken)
    {
        Documents.Add(new NoticeDocument
        {
            Id = Interlocked.Increment(ref _nextId),
            NoticeId = noticeId,
            Url = url,
            FileName = fileName,
            ExtractedText = text,
            DownloadedAt = fetchedAt
        });

        return Task.CompletedTask;
    }

    public Task MarkUnavailableAsync(long documentId, string reason, CancellationToken cancellationToken)
    {
        var index = Documents.FindIndex(d => d.Id == documentId);
        if (index >= 0)
        {
            Documents[index] = Documents[index] with { UnavailableReason = reason };
        }

        return Task.CompletedTask;
    }
}

internal static class TestSources
{
    public static Source Municipality(long id = 12, string? adapterConfig = null) => new()
    {
        Id = id,
        AuthorityName = "Çankaya Belediyesi",
        AuthorityType = AuthorityType.Municipality,
        Province = "Ankara",
        Url = "https://example.gov.tr/ihaleler",
        AdapterKey = "generic_table",
        AdapterConfig = adapterConfig
    };
}
