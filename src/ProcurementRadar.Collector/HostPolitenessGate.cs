using System.Collections.Concurrent;

namespace ProcurementRadar.Collector;

/// <summary>
/// Serialises every request per host and keeps them apart in time.
/// </summary>
/// <remarks>
/// Two rules, both from CLAUDE.md and both non-negotiable: one request in flight
/// per host, and a minimum gap plus jitter between them. The gap is measured
/// from when the previous request finished, not when it started, so a slow reply
/// cannot be followed immediately by the next one.
/// </remarks>
public sealed class HostPolitenessGate : IDisposable
{
    private readonly ConcurrentDictionary<string, HostState> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _timeProvider;
    private readonly HttpFetcherOptions _options;
    private readonly Func<TimeSpan> _nextJitter;

    public HostPolitenessGate(
        TimeProvider timeProvider,
        HttpFetcherOptions options,
        Func<TimeSpan>? nextJitter = null)
    {
        _timeProvider = timeProvider;
        _options = options;
        _nextJitter = nextJitter ?? DefaultJitter(options);
    }

    public async Task<T> RunAsync<T>(
        string host,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var state = _hosts.GetOrAdd(host, _ => new HostState());

        await state.InFlight.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = WaitBefore(state.LastRequestFinishedAt);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _timeProvider, cancellationToken).ConfigureAwait(false);
            }

            return await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            state.LastRequestFinishedAt = _timeProvider.GetUtcNow();
            state.InFlight.Release();
        }
    }

    internal TimeSpan WaitBefore(DateTimeOffset? lastRequestFinishedAt)
    {
        var jitter = _nextJitter();

        if (lastRequestFinishedAt is null)
        {
            // Nothing has been asked of this host yet, so there is nothing to
            // wait for. The jitter still applies: a restart must not fire every
            // source at the same instant.
            return jitter;
        }

        var elapsed = _timeProvider.GetUtcNow() - lastRequestFinishedAt.Value;
        var remaining = _options.MinimumHostInterval - elapsed;

        return remaining > TimeSpan.Zero ? remaining + jitter : jitter;
    }

    private static Func<TimeSpan> DefaultJitter(HttpFetcherOptions options)
    {
        var maximum = (int)options.MaximumJitter.TotalMilliseconds;

        return () => TimeSpan.FromMilliseconds(Random.Shared.Next(0, maximum + 1));
    }

    public void Dispose()
    {
        foreach (var state in _hosts.Values)
        {
            state.InFlight.Dispose();
        }

        _hosts.Clear();
    }

    private sealed class HostState
    {
        public SemaphoreSlim InFlight { get; } = new(1, 1);

        public DateTimeOffset? LastRequestFinishedAt { get; set; }
    }
}
