namespace ProcurementRadar.Worker;

/// <summary>
/// Runs the award sweep on a slow clock.
/// </summary>
/// <remarks>
/// Six hours, five İKNs. The numbers are chosen against the poll loop rather than
/// against how fast results could be collected: every one of these is a browser
/// through the same politeness gate EKAP's search uses, and a notice with a
/// deadline tonight has a better claim on that gate than a contract signed in June.
/// At this rate a backlog of a few hundred concluded tenders clears in a fortnight
/// and then the sweep mostly finds nothing to do, which is the steady state.
/// </remarks>
internal sealed class AwardWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    // The result announcement follows the signed contract, not the deadline. Two
    // weeks is early enough to catch the quick ones and late enough not to spend
    // every attempt on tenders that are still being evaluated.
    private const int RipeDays = 14;
    private const int MaximumAttempts = 6;
    private const int BatchSize = 5;

    private readonly AwardSweeper _sweeper;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AwardWorker> _logger;

    public AwardWorker(AwardSweeper sweeper, TimeProvider timeProvider, ILogger<AwardWorker> logger)
    {
        _sweeper = sweeper;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Award worker started");

        // Lets the collector have the first minutes after a restart. The machine
        // is a laptop that gets shut every night (D-10), so start-up is a
        // recurring event and not a one-off.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(3), _timeProvider, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _sweeper
                    .RunAsync(RipeDays, MaximumAttempts, BatchSize, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Award sweep failed");
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

        _logger.LogInformation("Award worker stopped");
    }
}
