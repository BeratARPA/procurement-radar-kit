using ProcurementRadar.Notifier;

namespace ProcurementRadar.Worker;

/// <summary>Sends the messages that make the whole thing useful.</summary>
internal sealed class NotificationWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly NotificationService _notifications;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NotificationWorker> _logger;

    public NotificationWorker(
        NotificationService notifications,
        TimeProvider timeProvider,
        ILogger<NotificationWorker> logger)
    {
        _notifications = notifications;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Notification worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _notifications.RunAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Notification run failed");
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

        _logger.LogInformation("Notification worker stopped");
    }
}
