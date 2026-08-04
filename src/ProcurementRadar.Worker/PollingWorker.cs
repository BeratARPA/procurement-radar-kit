namespace ProcurementRadar.Worker;

/// <summary>
/// Host skeleton. Source polling arrives with T-05, the parse pipeline with
/// T-10, notifications with T-14 and health checks with T-15.
/// </summary>
internal sealed class PollingWorker : BackgroundService
{
    private readonly ILogger<PollingWorker> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public PollingWorker(ILogger<PollingWorker> logger, IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _lifetime = lifetime;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker started");

        // No source is registered yet, so there is nothing to poll. The host
        // reports itself and shuts down rather than idling forever. Replace this
        // with the PeriodicTimer loop when the collector lands in T-05.
        _logger.LogInformation("No polling schedule is configured yet, shutting down");
        _lifetime.StopApplication();

        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Worker stopped");
    }
}
