using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Adapters;
using ProcurementRadar.Domain.Repositories;

namespace ProcurementRadar.Worker;

/// <summary>
/// Visits the sources that are due and hands what comes back to the archive.
/// </summary>
/// <remarks>
/// The sweep runs often; whether a given source is actually asked anything is
/// decided by <see cref="PollIntervalCalculator"/>, and the pacing between
/// requests by the politeness gate inside the fetcher. Nothing here needs to
/// know about either.
/// </remarks>
internal sealed class CollectionWorker : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly ISourceRepository _sources;
    private readonly IRawFetchRepository _rawFetches;
    private readonly ISourceAdapterRegistry _adapters;
    private readonly RawFetchRecorder _recorder;
    private readonly PollIntervalCalculator _schedule;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CollectionWorker> _logger;

    public CollectionWorker(
        ISourceRepository sources,
        IRawFetchRepository rawFetches,
        ISourceAdapterRegistry adapters,
        RawFetchRecorder recorder,
        PollIntervalCalculator schedule,
        TimeProvider timeProvider,
        ILogger<CollectionWorker> logger)
    {
        _sources = sources;
        _rawFetches = rawFetches;
        _adapters = adapters;
        _recorder = recorder;
        _schedule = schedule;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Collection worker started");

        // D-10: the first sweep happens straight away. After a machine that was
        // off overnight, waiting out an interval before looking is exactly the
        // wrong thing to do.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // One bad sweep must not take the worker down; systemd would
                // restart it into the same failure.
                _logger.LogError(exception, "Collection sweep failed");
            }

            try
            {
                await Task.Delay(SweepInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Collection worker stopped");
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        var sources = await _sources.ListActiveAsync(cancellationToken).ConfigureAwait(false);
        var visited = 0;

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_schedule.IsDue(source.LastFetchAt, source.PollIntervalMinutes))
            {
                continue;
            }

            var adapter = _adapters.For(source.AdapterKey);
            if (adapter is null)
            {
                _logger.LogError(
                    "Source {SourceId} wants adapter '{AdapterKey}', which nothing provides",
                    source.Id,
                    source.AdapterKey);

                continue;
            }

            // Sent back so an unchanged page answers 304 and costs no body.
            var latest = await _rawFetches.GetLatestAsync(source.Id, cancellationToken).ConfigureAwait(false);
            var conditional = latest is null
                ? ConditionalRequest.None
                : new ConditionalRequest(latest.ETag, latest.LastModified);

            var result = await adapter.FetchAsync(source, conditional, cancellationToken).ConfigureAwait(false);
            await _recorder.RecordAsync(source, result, cancellationToken).ConfigureAwait(false);

            visited++;
        }

        if (visited > 0)
        {
            _logger.LogInformation("Swept {Visited} of {Total} active source(s)", visited, sources.Count);
        }
    }
}
