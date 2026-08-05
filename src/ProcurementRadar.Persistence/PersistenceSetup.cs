using Dapper;
using ProcurementRadar.Domain.Enums;

namespace ProcurementRadar.Persistence;

/// <summary>
/// One-time Dapper configuration. Call this once from the composition root
/// before opening any connection.
/// </summary>
public static class PersistenceSetup
{
    private static readonly object Gate = new();
    private static bool _registered;

    /// <summary>
    /// Teaches Dapper how to move the domain enums to and from the snake_case
    /// text columns the schema uses. Dapper's handler table is process-wide, so
    /// this is idempotent and safe to call from several hosts.
    /// </summary>
    public static void RegisterTypeHandlers()
    {
        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            SqlMapper.AddTypeHandler(new SnakeCaseEnumTypeHandler<AuthorityType>());
            SqlMapper.AddTypeHandler(new SnakeCaseEnumTypeHandler<ProcurementType>());
            SqlMapper.AddTypeHandler(new SnakeCaseEnumTypeHandler<NoticeStatus>());
            SqlMapper.AddTypeHandler(new SnakeCaseEnumTypeHandler<NotificationChannel>());

            _registered = true;
        }
    }
}
