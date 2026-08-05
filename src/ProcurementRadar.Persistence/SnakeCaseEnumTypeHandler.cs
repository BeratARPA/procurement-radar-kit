using System.Data;
using Dapper;
using ProcurementRadar.Domain.Enums;

namespace ProcurementRadar.Persistence;

/// <summary>
/// Moves an enum between the domain and the snake_case text column that stores
/// it. The conversion itself lives in <see cref="EnumText"/>, because the seed
/// files use the same spelling.
/// </summary>
internal sealed class SnakeCaseEnumTypeHandler<T> : SqlMapper.TypeHandler<T>
    where T : struct, Enum
{
    public override void SetValue(IDbDataParameter parameter, T value)
    {
        parameter.DbType = DbType.String;
        parameter.Value = EnumText.ToText(value);
    }

    public override T Parse(object value)
    {
        var text = value as string
            ?? throw new InvalidCastException(
                $"Cannot read {typeof(T).Name} from a {value?.GetType().Name ?? "null"} value.");

        if (EnumText.TryParse<T>(text, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"'{text}' is not a known {typeof(T).Name}. The database holds a value the code does not model.");
    }
}
