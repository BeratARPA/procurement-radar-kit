using Microsoft.Extensions.Logging;
using ProcurementRadar.Domain.Settings;

namespace ProcurementRadar.Collector;

/// <summary>
/// Fills the setting table from config/*.json, once, for keys it does not have.
/// </summary>
/// <remarks>
/// The direction matters and only goes one way. A file is what a fresh install
/// starts from; it is never an instruction to a system that has been running for
/// months with something else. Editing keywords.json on a machine whose database
/// already holds a keyword list changes nothing, and that is the intended
/// behaviour rather than an oversight - the alternative is a deploy silently
/// undoing what somebody set from their phone.
///
/// Reading files is why this lives here and not in Persistence: nothing on the
/// parse path is allowed to touch the disk, and settings are loaded once at
/// startup, well away from it.
/// </remarks>
public sealed class SettingSeeder
{
    private readonly ISettingRepository _settings;
    private readonly string _configDirectory;
    private readonly ILogger<SettingSeeder> _logger;

    public SettingSeeder(ISettingRepository settings, string configDirectory, ILogger<SettingSeeder> logger)
    {
        _settings = settings;
        _configDirectory = configDirectory;
        _logger = logger;
    }

    /// <summary>Returns how many keys were new.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var seeded = 0;

        foreach (var (key, fileName) in SettingKeys.Seeds)
        {
            string json;

            try
            {
                json = ConfigDirectory.Read(_configDirectory, fileName);
            }
            catch (Exception exception) when (exception is IOException or DirectoryNotFoundException)
            {
                // A missing file is only fatal if the database has no value
                // either, and that is the caller's problem to notice when it
                // reads the setting back. Stopping the worker here would mean a
                // renamed config file takes the collector down with it.
                _logger.LogWarning(exception, "Could not read {FileName} to seed setting {Key}", fileName, key);
                continue;
            }

            if (await _settings.InsertIfMissingAsync(key, json, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogInformation("Seeded setting {Key} from {FileName}", key, fileName);
                seeded++;
            }
        }

        _logger.LogInformation(
            "Settings: {Seeded} of {Total} seeded from config, the rest already stored",
            seeded,
            SettingKeys.Seeds.Count);

        return seeded;
    }
}
