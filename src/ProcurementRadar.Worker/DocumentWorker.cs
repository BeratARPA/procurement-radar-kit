using ProcurementRadar.Collector;

namespace ProcurementRadar.Worker;

/// <summary>
/// Fetches the specification files notices link to.
/// </summary>
/// <remarks>
/// Separate from parsing on purpose: this is the only part of document handling
/// that reaches the network, and keeping it out of the pipeline is what lets the
/// pipeline be re-run against the archive offline.
/// </remarks>
internal sealed class DocumentWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private const int BatchSize = 25;

    private readonly DocumentDownloader _downloader;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DocumentWorker> _logger;

    public DocumentWorker(
        DocumentDownloader downloader,
        TimeProvider timeProvider,
        ILogger<DocumentWorker> logger)
    {
        _downloader = downloader;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Document worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _downloader.RunAsync(BatchSize, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Document run failed");
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

        _logger.LogInformation("Document worker stopped");
    }
}
