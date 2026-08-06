using ProcurementRadar.Collector;
using ProcurementRadar.Notifier;

namespace ProcurementRadar.Worker;

/// <summary>Sends the messages that make the whole thing useful.</summary>
internal sealed class NotificationWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How many announcements one pass may fetch. Each is a browser round trip
    /// behind the politeness gate, so a backlog is worked through over several
    /// minutes rather than in one long block that delays every notification.
    /// </summary>
    private const int AnnouncementBatchSize = 3;

    private readonly NotificationService _notifications;
    private readonly EkapAnnouncementEnricher _announcements;
    private readonly NotificationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NotificationWorker> _logger;

    public NotificationWorker(
        NotificationService notifications,
        EkapAnnouncementEnricher announcements,
        NotificationOptions options,
        TimeProvider timeProvider,
        ILogger<NotificationWorker> logger)
    {
        _notifications = notifications;
        _announcements = announcements;
        _options = options;
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
                // Before the message, not after: the announcement is what turns
                // "yazılım geliştirme" into something a person can judge, and it
                // is worth the browser round trip only for notices that are about
                // to be sent.
                await _announcements
                    .RunAsync(_options.MinimumScore, AnnouncementBatchSize, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A notification without its announcement is still worth sending.
                _logger.LogError(exception, "Announcement run failed; announcing anyway");
            }

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
