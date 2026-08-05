using System.Data.Common;
using Npgsql;

namespace ProcurementRadar.Persistence;

/// <summary>
/// Owns a single pooled <see cref="NpgsqlDataSource"/> for the process. Register
/// it as a singleton; opening a data source per call would throw the pool away.
/// </summary>
public sealed class NpgsqlConnectionFactory : IDbConnectionFactory, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public NpgsqlConnectionFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
        => await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
