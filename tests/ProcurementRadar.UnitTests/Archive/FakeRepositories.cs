using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Domain.Repositories;

namespace ProcurementRadar.UnitTests.Archive;

/// <summary>
/// In-memory stand-ins. D-09 rules out integration tests, so the archive rules
/// are checked against these rather than against PostgreSQL.
/// </summary>
internal sealed class FakeRawFetchRepository : IRawFetchRepository
{
    private long _nextId;

    public List<RawFetch> Rows { get; } = [];

    public Task<long> ReserveIdAsync(CancellationToken cancellationToken)
        => Task.FromResult(Interlocked.Increment(ref _nextId));

    public Task InsertAsync(RawFetch rawFetch, CancellationToken cancellationToken)
    {
        Rows.Add(rawFetch);
        return Task.CompletedTask;
    }

    public Task<string?> GetLatestContentHashAsync(long sourceId, CancellationToken cancellationToken)
        => Task.FromResult(Rows
            .Where(row => row.SourceId == sourceId)
            .OrderByDescending(row => row.FetchedAt)
            .ThenByDescending(row => row.Id)
            .Select(row => row.ContentHash)
            .FirstOrDefault());

    public Task<IReadOnlyList<RawFetch>> ListUnprocessedAsync(
        long? sourceId,
        int limit,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<RawFetch>>(Rows
            .Where(row => !row.IsProcessed && (sourceId is null || row.SourceId == sourceId))
            .OrderBy(row => row.FetchedAt)
            .Take(limit)
            .ToList());

    public Task MarkProcessedAsync(long rawFetchId, string parserVersion, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

internal sealed class FakeSourceRepository : ISourceRepository
{
    public int SuccessCount { get; private set; }

    public int FailureCount { get; private set; }

    public Task<IReadOnlyList<Source>> ListActiveAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Source>>([]);

    public Task<bool> UpsertAsync(Source source, CancellationToken cancellationToken)
        => Task.FromResult(true);

    public Task RecordFetchSuccessAsync(long sourceId, bool foundNewNotice, CancellationToken cancellationToken)
    {
        SuccessCount++;
        return Task.CompletedTask;
    }

    public Task RecordFetchFailureAsync(long sourceId, CancellationToken cancellationToken)
    {
        FailureCount++;
        return Task.CompletedTask;
    }

    public Task RecordRobotsCheckAsync(long sourceId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task DeactivateAsync(long sourceId, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

internal static class TestSources
{
    public static Source Municipality(long id = 12) => new()
    {
        Id = id,
        AuthorityName = "Çankaya Belediyesi",
        AuthorityType = AuthorityType.Municipality,
        Province = "Ankara",
        Url = "https://example.gov.tr/ihaleler",
        AdapterKey = "generic_table"
    };
}
