using ProcurementRadar.Notifier;

namespace ProcurementRadar.Worker;

/// <summary>
/// Listens for the two buttons.
/// </summary>
/// <remarks>
/// Long polling rather than a webhook: a webhook needs a public address, and
/// D-10 puts this on a machine at home with no domain and nothing exposed.
/// The call blocks until Telegram has something to say, so the loop needs no
/// delay of its own.
/// </remarks>
internal sealed class CallbackWorker : BackgroundService
{
    private static readonly TimeSpan PauseAfterFailure = TimeSpan.FromSeconds(30);

    private readonly CallbackProcessor _callbacks;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CallbackWorker> _logger;

    public CallbackWorker(
        CallbackProcessor callbacks,
        TimeProvider timeProvider,
        ILogger<CallbackWorker> logger)
    {
        _callbacks = callbacks;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Callback worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _callbacks.RunAsync(stoppingToken).ConfigureAwait(false);
                continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Reading button presses failed");
            }

            try
            {
                await Task.Delay(PauseAfterFailure, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Callback worker stopped");
    }
}
