namespace ProcurementRadar.Domain.Enums;

/// <summary>
/// Converts enum members to the snake_case text used by both the schema and the
/// config files, and back. <c>ProvincialDirectorate</c> becomes
/// <c>provincial_directorate</c>.
/// </summary>
/// <remarks>
/// The text is derived from the member name rather than listed, so nothing has
/// to be kept in sync by hand. The price is that renaming a member silently
/// changes what is written to the database, which is why every member of every
/// mapped enum is pinned to its exact text by a test.
/// </remarks>
public static class EnumText
{
    public static string ToText<T>(T value)
        where T : struct, Enum
    {
        var name = value.ToString();
        var builder = new System.Text.StringBuilder(name.Length + 4);

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

    public static bool TryParse<T>(string? text, out T value)
        where T : struct, Enum
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (var candidate in Enum.GetValues<T>())
            {
                if (string.Equals(ToText(candidate), text, StringComparison.Ordinal))
                {
                    value = candidate;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
