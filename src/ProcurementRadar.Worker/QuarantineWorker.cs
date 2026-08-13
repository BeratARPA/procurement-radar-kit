using ProcurementRadar.Notifier;

namespace ProcurementRadar.Worker;

/// <summary>
/// Runs the quarantine sweep on the slowest clock in the system.
/// </summary>
/// <remarks>
/// Once a day, five sources. Chosen against the poll loop rather than against how
/// fast a dead source could be rediscovered: every probe is a real request through
/// the same politeness gate the collector uses, and a source with a deadline
/// tonight has a better claim on that gate than one that has been gone since
/// August. At this rate the whole quarantine list is asked within a few days and
/// then the sweep mostly finds nothing, which is the steady state.
///
/// A day is also the right unit for what is being watched. The conditions that put
/// a source in here - an expired hosting account, a dns record that stopped
/// resolving, an origin server behind a 522 - are fixed by a person somewhere on
/// a scale of days. Asking hourly would only make the traffic look impatient.
/// </remarks>
internal sealed class QuarantineWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private const int BatchSize = 5;

    private readonly QuarantineSweeper _sweeper;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<QuarantineWorker> _logger;

    public QuarantineWorker(
        QuarantineSweeper sweeper,
        TimeProvider timeProvider,
        ILogger<QuarantineWorker> logger)
    {
        _sweeper = sweeper;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Quarantine worker started");

        // Lets the collector have the first minutes after a restart. The machine
        // is a laptop that gets shut every night (D-10), so start-up is a
        // recurring event; without this the sweep would compete with the catch-up
        // poll every single morning.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), _timeProvider, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _sweeper.RunAsync(BatchSize, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Quarantine sweep failed");
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

        _logger.LogInformation("Quarantine worker stopped");
    }
}
