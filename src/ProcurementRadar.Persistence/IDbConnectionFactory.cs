using System.Data.Common;

namespace ProcurementRadar.Persistence;

/// <summary>Hands out open connections. Callers dispose what they receive.</summary>
public interface IDbConnectionFactory
{
    Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken);
}
