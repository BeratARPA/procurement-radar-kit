using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using ProcurementRadar.Domain.Fetching;

namespace ProcurementRadar.Collector;

/// <summary>
/// The one door to the network. Everything the politeness rules demand happens
/// here so no caller has to remember them.
/// </summary>
public sealed class HttpFetcher : IHttpFetcher, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly HttpFetcherOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HttpFetcher> _logger;
    private readonly HostPolitenessGate _gate;
    private readonly RobotsGuard _robots;
    private readonly ResiliencePipeline<FetchResult> _pipeline;

    public HttpFetcher(
        HttpClient httpClient,
        HttpFetcherOptions options,
        TimeProvider timeProvider,
        ILogger<HttpFetcher> logger,
        Func<TimeSpan>? nextJitter = null)
    {
        _httpClient = httpClient;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);

        _gate = new HostPolitenessGate(timeProvider, options, nextJitter);
        _robots = new RobotsGuard(httpClient, _gate, timeProvider, options, logger);
        _pipeline = BuildPipeline(options, logger);
    }

    public async Task<FetchResult> FetchAsync(FetchRequest request, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _logger.LogError("Refusing to fetch {Url}: not an absolute http or https url", request.Url);
            return FetchResult.Failed(0);
        }

        if (!await _robots.IsAllowedAsync(uri, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("robots.txt disallows {Url}; skipping this source", uri);
            return FetchResult.SkippedByRobots();
        }

        // The retry sits outside the gate on purpose: every attempt queues behind
        // the same per-host lock and waits out the minimum interval again, so a
        // retry storm cannot turn into a burst.
        return await _pipeline.ExecuteAsync(
            async token => await _gate
                .RunAsync(uri.Authority, inner => SendAsync(uri, request, inner), token)
                .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<FetchResult> SendAsync(Uri uri, FetchRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, uri);

        if (!string.IsNullOrWhiteSpace(request.ETag))
        {
            message.Headers.TryAddWithoutValidation("If-None-Match", request.ETag);
        }

        if (!string.IsNullOrWhiteSpace(request.LastModified))
        {
            message.Headers.TryAddWithoutValidation("If-Modified-Since", request.LastModified);
        }

        using var timeout = new CancellationTokenSource(_options.RequestTimeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            // ResponseHeadersRead, not the default: on a 304 the body must not be
            // downloaded, and the default option buffers it before we get a say.
            using var response = await _httpClient
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                .ConfigureAwait(false);

            var status = (int)response.StatusCode;
            var etag = response.Headers.ETag?.ToString();
            var lastModified = response.Content.Headers.LastModified?.ToString("R", CultureInfo.InvariantCulture);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                _logger.LogInformation("{Url} is unchanged (304)", uri);

                // Nothing is read from response.Content. Not downloading it is the
                // entire point of sending the conditional headers.
                return FetchResult.NotModified(etag ?? request.ETag, lastModified ?? request.LastModified);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("{Url} answered {StatusCode}", uri, status);
                return FetchResult.Failed(status);
            }

            // The bytes are handed on exactly as they arrived (D-11). Deciding
            // the character encoding here would bake that decision into the
            // archive, and a wrong guess could then never be repaired by
            // reprocessing. The Content-Type travels with them because the
            // charset it may carry is not recoverable from the body itself.
            var bytes = await response.Content.ReadAsByteArrayAsync(linked.Token).ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.ToString();
            var contentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            _logger.LogInformation(
                "Fetched {Url}: {StatusCode}, {ByteCount} bytes, {ContentType}",
                uri,
                status,
                bytes.Length,
                contentType);

            return new FetchResult(FetchOutcome.Fetched, status, bytes, etag, lastModified, contentHash, contentType);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "{Url} could not be reached", uri);
            return FetchResult.Failed(0);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("{Url} timed out after {Timeout}", uri, _options.RequestTimeout);
            return FetchResult.Failed(0);
        }
    }

    private static ResiliencePipeline<FetchResult> BuildPipeline(
        HttpFetcherOptions options,
        ILogger logger)
        => new ResiliencePipelineBuilder<FetchResult>()
            .AddRetry(new RetryStrategyOptions<FetchResult>
            {
                MaxRetryAttempts = Math.Max(0, options.MaximumAttempts - 1),
                BackoffType = DelayBackoffType.Exponential,
                Delay = options.RetryBaseDelay,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<FetchResult>()
                    .HandleResult(result => result.Outcome == FetchOutcome.Failed
                                            && IsWorthRetrying(result.HttpStatus)),
                OnRetry = arguments =>
                {
                    logger.LogWarning(
                        "Retrying after {StatusCode}, attempt {AttemptNumber}",
                        arguments.Outcome.Result?.HttpStatus,
                        arguments.AttemptNumber + 1);

                    return default;
                }
            })
            .Build();

    /// <summary>
    /// A retry is only worth it when the failure looks like the server's problem
    /// or the network's. A 404 will still be a 404 five seconds later.
    /// </summary>
    private static bool IsWorthRetrying(int httpStatus)
        => httpStatus == 0
            || httpStatus >= 500
            || httpStatus == (int)HttpStatusCode.RequestTimeout
            || httpStatus == (int)HttpStatusCode.TooManyRequests;

    public void Dispose() => _gate.Dispose();
}
