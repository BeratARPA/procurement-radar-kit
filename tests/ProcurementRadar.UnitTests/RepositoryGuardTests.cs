using System.Data.Common;
using ProcurementRadar.Persistence;

namespace ProcurementRadar.UnitTests;

/// <summary>
/// Argument checks have to fire before a connection is taken from the pool. The
/// factory used here throws on contact, so any test that passes proves the guard
/// ran first.
/// </summary>
public sealed class RepositoryGuardTests
{
    [Fact]
    public async Task Listing_unprocessed_rows_rejects_a_limit_of_zero()
    {
        var repository = new RawFetchRepository(new UnusableConnectionFactory());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => repository.ListUnprocessedAsync(sourceId: null, limit: 0, CancellationToken.None));
    }

    [Fact]
    public async Task Marking_a_row_processed_rejects_a_blank_parser_version()
    {
        var repository = new RawFetchRepository(new UnusableConnectionFactory());

        await Assert.ThrowsAsync<ArgumentException>(
            () => repository.MarkProcessedAsync(rawFetchId: 1, parserVersion: "  ", CancellationToken.None));
    }

    [Fact]
    public void The_connection_factory_rejects_a_blank_connection_string()
    {
        Assert.Throws<ArgumentException>(() => new NpgsqlConnectionFactory("   "));
    }

    private sealed class UnusableConnectionFactory : IDbConnectionFactory
    {
        public Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "A guard clause should have run before any connection was opened.");
    }
}
