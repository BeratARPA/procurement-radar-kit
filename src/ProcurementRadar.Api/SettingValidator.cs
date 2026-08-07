using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Normalization;
using ProcurementRadar.Domain.Settings;
using ProcurementRadar.Notifier;
using ProcurementRadar.Parsers;
using ProcurementRadar.Scoring;

namespace ProcurementRadar.Api;

/// <summary>
/// Refuses a setting the worker would not be able to load.
/// </summary>
/// <remarks>
/// The worker already survives a bad value - it logs an error and keeps the
/// previous one - but surviving is not the same as being told. Somebody editing
/// a keyword list from a phone gets an answer here, at the moment they press
/// save, instead of finding out a week later that nothing has been notified.
///
/// The check is the loader itself, not a second description of what valid looks
/// like. Two validators drift; one cannot.
/// </remarks>
public static class SettingValidator
{
    private static readonly Dictionary<string, Action<string>> Loaders = new(StringComparer.Ordinal)
    {
        [SettingKeys.Keywords] = json => KeywordRules.Load(json),
        [SettingKeys.Messages] = json => MessageTemplates.Load(json),
        [SettingKeys.DatePatterns] = json => TurkishDatePatterns.Load(json),
        [SettingKeys.Normalization] = json => NormalizationRules.Load(json),
        [SettingKeys.PersonalData] = json => PersonalDataRules.Load(json),
        [SettingKeys.AwardLabels] = json => AwardLabels.Load(json),

        // The one with floors rather than a shape: five seconds between
        // requests to a host, and robots.txt obeyed. Refused here and
        // refused again in the worker (D-16).
        [SettingKeys.Politeness] = json => PolitenessSettings.Load(json)
    };

    public static bool IsKnown(string key)
        => SettingKeys.Seeds.Any(seed => string.Equals(seed.Key, key, StringComparison.Ordinal));

    /// <summary>Null when the value is fine, otherwise why it is not.</summary>
    public static string? Validate(string key, string json)
    {
        if (!IsKnown(key))
        {
            return $"'{key}' is not a setting this system has.";
        }

        if (!Loaders.TryGetValue(key, out var load))
        {
            // ekap_ui has no loader of its own - it is read field by field where
            // it is used. Nothing to check beyond it being json.
            try
            {
                using var _ = System.Text.Json.JsonDocument.Parse(json);

                return null;
            }
            catch (System.Text.Json.JsonException exception)
            {
                return exception.Message;
            }
        }

        try
        {
            load(json);

            return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return exception.Message;
        }
    }
}
