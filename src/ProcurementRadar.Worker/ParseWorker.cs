using ProcurementRadar.Collector;

namespace ProcurementRadar.Worker;

/// <summary>
/// Feeds archived bodies through the parsers. Runs separately from collection so
/// a slow or failing site cannot hold up work that is purely local.
/// </summary>
internal sealed class ParseWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private const int BatchSize = 200;

    private readonly ParsePipeline _pipeline;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ParseWorker> _logger;

    public ParseWorker(ParsePipeline pipeline, TimeProvider timeProvider, ILogger<ParseWorker> logger)
    {
        _pipeline = pipeline;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Parse worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _pipeline.RunAsync(null, BatchSize, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Parse run failed");
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

        _logger.LogInformation("Parse worker stopped");
    }
}
