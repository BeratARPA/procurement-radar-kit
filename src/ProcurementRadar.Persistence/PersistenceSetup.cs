using ProcurementRadar.Domain.Enums;

namespace ProcurementRadar.Persistence;

/// <summary>
/// How enums cross the boundary. There is nothing to register.
/// </summary>
/// <remarks>
/// Dapper handles enums itself, before it ever looks at a type handler, and it
/// does so by name in both directions: an enum parameter becomes its underlying
/// integer, and an enum column is read with Enum.Parse against the member names.
/// Neither matches the snake_case text the schema stores, and a registered
/// handler is simply ignored - which is worse than none, because it reads as if
/// the problem were solved.
///
/// So both directions are explicit instead:
///
///   writing - the repositories pass <see cref="EnumText.ToText"/>, so
///             AuthorityType.Municipality is sent as "municipality" rather than
///             as 1;
///   reading - the SELECT converts the stored text to the member name, so
///             "provincial_directorate" arrives as "ProvincialDirectorate" and
///             Enum.Parse finds it.
///
/// Single-word values such as "municipality" or "new" happen to work without any
/// of this, which is exactly why the gap stayed hidden until a value with an
/// underscore was read.
/// </remarks>
public static class PersistenceSetup
{
    /// <summary>
    /// Kept so the composition roots have one obvious place to call, and so this
    /// explanation has somewhere to live.
    /// </summary>
    public static void RegisterTypeHandlers()
    {
    }
}
