using System.Data;
using System.Text;
using Dapper;

namespace ProcurementRadar.Persistence;

/// <summary>
/// Maps an enum member to the snake_case text the schema stores, and back.
/// <c>ProvincialDirectorate</c> becomes <c>provincial_directorate</c>.
/// </summary>
/// <remarks>
/// The conversion is derived rather than listed, so nothing has to be kept in
/// sync by hand. The price is that a rename silently changes what is written to
/// the database, which is why every member of every mapped enum is pinned to its
/// exact database string by a test.
/// </remarks>
internal sealed class SnakeCaseEnumTypeHandler<T> : SqlMapper.TypeHandler<T>
    where T : struct, Enum
{
    public override void SetValue(IDbDataParameter parameter, T value)
    {
        parameter.DbType = DbType.String;
        parameter.Value = ToDatabaseText(value);
    }

    public override T Parse(object value)
    {
        var text = value as string
            ?? throw new InvalidCastException(
                $"Cannot read {typeof(T).Name} from a {value?.GetType().Name ?? "null"} value.");

        foreach (var candidate in Enum.GetValues<T>())
        {
            if (string.Equals(ToDatabaseText(candidate), text, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"'{text}' is not a known {typeof(T).Name}. The database holds a value the code does not model.");
    }

    internal static string ToDatabaseText(T value)
    {
        var name = value.ToString();
        var builder = new StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }
}
