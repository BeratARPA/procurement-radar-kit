using ProcurementRadar.Collector;

namespace ProcurementRadar.Worker;

/// <summary>
/// Seeds the settings once, then watches for changes to them.
/// </summary>
/// <remarks>
/// Thirty seconds is chosen against what it is for: somebody changes the
/// threshold on their phone, puts the phone down, and wants the next notice
/// scored the new way. Half a minute is short enough that it feels immediate and
/// long enough that the question - one max() over a dozen rows - costs nothing
/// worth measuring.
///
/// Runs before the other workers get anywhere: seeding first means a fresh
/// install has its settings in the database before the first poll, and the first
/// refresh replaces the file-loaded fallbacks with whatever is really stored.
/// </remarks>
internal sealed class SettingsWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly SettingSeeder _seeder;
    private readonly SettingsRefresher _refresher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SettingsWorker> _logger;

    public SettingsWorker(
        SettingSeeder seeder,
        SettingsRefresher refresher,
        TimeProvider timeProvider,
        ILogger<SettingsWorker> logger)
    {
        _seeder = seeder;
        _refresher = refresher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Settings worker started");

        try
        {
            await _seeder.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            // The fallbacks read from the config files are already in place, so
            // the system runs on exactly what it ran on before this worker
            // existed. Worth an error, not worth a stop.
            _logger.LogError(exception, "Settings could not be seeded; running on the config files");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _refresher.RunAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Settings refresh failed");
            }

            try
            {
                await Task.Delay(Interval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Settings worker stopped");
    }
}
