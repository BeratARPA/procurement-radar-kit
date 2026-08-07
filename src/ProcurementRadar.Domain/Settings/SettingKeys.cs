namespace ProcurementRadar.Domain.Settings;

/// <summary>
/// Every setting the system has, and the config file each one starts from.
/// </summary>
/// <remarks>
/// One list, shared by the seeder that fills a fresh database, the worker that
/// watches for changes and the api that serves the settings screen. A key
/// spelled differently in any of the three is a setting that can be saved and
/// never read.
/// </remarks>
public static class SettingKeys
{
    public const string Keywords = "keywords";
    public const string Messages = "messages";
    public const string DatePatterns = "date_patterns";
    public const string Normalization = "normalization";
    public const string PersonalData = "personal_data";
    public const string AwardLabels = "award_labels";
    public const string EkapUi = "ekap_ui";
    public const string Politeness = "politeness";

    /// <summary>Key to config file name, in the order a settings screen should show them.</summary>
    public static IReadOnlyList<(string Key, string FileName)> Seeds { get; } =
    [
        (Keywords, "keywords.json"),
        (Messages, "tr-messages.json"),
        (DatePatterns, "tr-date-patterns.json"),
        (Normalization, "tr-normalization.json"),
        (PersonalData, "tr-personal-data.json"),
        (AwardLabels, "tr-award-labels.json"),
        (EkapUi, "ekap-ui.json"),
        (Politeness, "politeness.json")
    ];
}
