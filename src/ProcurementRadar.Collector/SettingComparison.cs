using System.Text.Json;

namespace ProcurementRadar.Collector;

/// <summary>
/// Whether a stored setting still says what its config file says.
/// </summary>
public static class SettingComparison
{
    /// <summary>
    /// Whether two json documents say the same thing, formatting and key order
    /// aside.
    /// </summary>
    /// <remarks>
    /// Order matters here and it is not a detail. PostgreSQL's jsonb does not
    /// keep the order the keys were written in, so a value read back is almost
    /// never byte-identical to the file it was seeded from. Comparing the
    /// serialized documents said all eight settings had diverged when two had -
    /// which is precisely the crying wolf this comparison exists to avoid.
    ///
    /// So both sides are canonicalised: objects sorted by key, arrays left in
    /// place because their order is content.
    /// </remarks>
    public static bool SameJson(string left, string right)
    {
        try
        {
            using var a = JsonDocument.Parse(left);
            using var b = JsonDocument.Parse(right);

            return string.Equals(Canonical(a.RootElement), Canonical(b.RootElement), StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Canonical(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = element
                    .EnumerateObject()
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => JsonSerializer.Serialize(property.Name) + ":" + Canonical(property.Value));

                return "{" + string.Join(",", properties) + "}";

            case JsonValueKind.Array:
                return "[" + string.Join(",", element.EnumerateArray().Select(Canonical)) + "]";

            default:
                return element.GetRawText();
        }
    }
}
