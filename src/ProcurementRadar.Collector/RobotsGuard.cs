using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ProcurementRadar.Collector;

/// <summary>
/// Reads and caches robots.txt, and answers whether a url may be requested.
/// </summary>
public sealed class RobotsGuard
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _httpClient;
    private readonly HostPolitenessGate _gate;
    private readonly TimeProvider _timeProvider;
    private readonly HttpFetcherOptions _options;
    private readonly ILogger _logger;

    public RobotsGuard(
        HttpClient httpClient,
        HostPolitenessGate gate,
        TimeProvider timeProvider,
        HttpFetcherOptions options,
        ILogger logger)
    {
        _httpClient = httpClient;
        _gate = gate;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    public async Task<bool> IsAllowedAsync(Uri uri, CancellationToken cancellationToken)
    {
        var rules = await RulesForAsync(uri, cancellationToken).ConfigureAwait(false);

        return rules.IsAllowed(uri.PathAndQuery);
    }

    private async Task<RobotsRules> RulesForAsync(Uri uri, CancellationToken cancellationToken)
    {
        var origin = uri.GetLeftPart(UriPartial.Authority);

        if (TryGetFresh(origin, out var cached))
        {
            return cached;
        }

        // Fetching robots.txt is itself a request to the host, so it goes through
        // the same gate. The gate serialises per host, which also means the
        // second caller to arrive finds the cache filled by the first.
        return await _gate.RunAsync(
            uri.Authority,
            async token =>
            {
                if (TryGetFresh(origin, out var justFilled))
                {
                    return justFilled;
                }

                var (rules, lifetime) = await DownloadAsync(origin, token).ConfigureAwait(false);
                _cache[origin] = new CacheEntry(rules, _timeProvider.GetUtcNow() + lifetime);

                return rules;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<(RobotsRules Rules, TimeSpan Lifetime)> DownloadAsync(
        string origin,
        CancellationToken cancellationToken)
    {
        var robotsUrl = $"{origin}/robots.txt";

        try
        {
            using var response = await _httpClient
                .GetAsync(robotsUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Read robots.txt for {Origin}", origin);

                return (RobotsRules.Parse(content, _options.RobotsUserAgentToken), _options.RobotsCacheDuration);
            }

            if ((int)response.StatusCode is >= 400 and < 500)
            {
                // No robots.txt at all. RFC 9309 says that means everything is
                // permitted, and it is by far the most common case.
                _logger.LogInformation(
                    "No robots.txt at {Origin} ({StatusCode}), treating the host as open",
                    origin,
                    (int)response.StatusCode);

                return (RobotsRules.AllowAll, _options.RobotsCacheDuration);
            }

            _logger.LogWarning(
                "robots.txt at {Origin} answered {StatusCode}; treating the host as closed until {Retry} from now",
                origin,
                (int)response.StatusCode,
                _options.RobotsRetryDuration);

            return (RobotsRules.DenyAll, _options.RobotsRetryDuration);
        }
        catch (HttpRequestException exception)
        {
            // Erring towards not crawling: an unreachable robots.txt is exactly
            // the situation where a site is least able to cope with us.
            _logger.LogWarning(
                exception,
                "Could not read robots.txt at {Origin}; treating the host as closed until {Retry} from now",
                origin,
                _options.RobotsRetryDuration);

            return (RobotsRules.DenyAll, _options.RobotsRetryDuration);
        }
    }

    private bool TryGetFresh(string origin, out RobotsRules rules)
    {
        if (_cache.TryGetValue(origin, out var entry) && entry.ExpiresAt > _timeProvider.GetUtcNow())
        {
            rules = entry.Rules;
            return true;
        }

        rules = RobotsRules.AllowAll;
        return false;
    }

    private sealed record CacheEntry(RobotsRules Rules, DateTimeOffset ExpiresAt);
}
