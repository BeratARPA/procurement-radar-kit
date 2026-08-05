using ProcurementRadar.Notifier;

namespace ProcurementRadar.Worker;

/// <summary>
/// Watches for the failures that make no noise.
/// </summary>
/// <remarks>
/// Runs whether or not Telegram is configured. A system nobody has finished
/// wiring up is exactly when a source goes silent unnoticed, so until there is a
/// bot the alarms go to the log.
/// </remarks>
internal sealed class HealthWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private readonly HealthMonitor _monitor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HealthWorker> _logger;

    public HealthWorker(HealthMonitor monitor, TimeProvider timeProvider, ILogger<HealthWorker> logger)
    {
        _monitor = monitor;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Health worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _monitor.RunAsync(stoppingToken).ConfigureAwait(false);
                await _monitor.TrySendWeeklySummaryAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Health check failed");
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

        _logger.LogInformation("Health worker stopped");
    }
}
