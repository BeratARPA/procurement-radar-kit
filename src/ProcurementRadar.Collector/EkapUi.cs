using System.Text.Json;

namespace ProcurementRadar.Collector;

/// <summary>
/// The labels EKAP's own interface uses, read from config/ekap-ui.json.
/// </summary>
/// <remarks>
/// They belong in config for the same reason every CSS selector in this project
/// does (CLAUDE.md rule 5): they describe somebody else's page. When EKAP renames
/// a button the fix is a data change, not a rebuild - and the file is the one
/// place to look for what the browser half depends on.
/// </remarks>
public static class EkapUi
{
    private const string FileName = "ekap-ui.json";

    public static string AnnouncementButtonName(string? configDirectory)
        => Read(configDirectory, "announcement_button_name");

    private static string Read(string? configDirectory, string key)
    {
        try
        {
            var json = ConfigDirectory.Read(ConfigDirectory.Find(configDirectory), FileName);

            if (JsonDocument.Parse(json).RootElement.TryGetProperty(key, out var value)
                && value.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (Exception exception)
            when (exception is IOException or JsonException or InvalidOperationException)
        {
            // Falls through to the throw below, which names the file.
        }

        throw new InvalidOperationException(
            $"config/{FileName} does not carry '{key}'. The EKAP browser step cannot run without it.");
    }
}
