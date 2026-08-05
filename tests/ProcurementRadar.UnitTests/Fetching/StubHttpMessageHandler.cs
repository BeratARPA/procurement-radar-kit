using System.Net;

namespace ProcurementRadar.UnitTests.Fetching;

internal sealed record RecordedRequest(Uri Uri, DateTimeOffset At, HttpRequestHeaders Headers, string? Body);

internal sealed record HttpRequestHeaders(string? IfNoneMatch, string? IfModifiedSince, string? UserAgent);

/// <summary>
/// Stands in for the network. Records what was asked for and when, using the
/// same clock the fetcher runs on, so tests can reason about spacing.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly TimeProvider _timeProvider;
    private readonly Func<Uri, int, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(TimeProvider timeProvider, Func<Uri, int, HttpResponseMessage> responder)
    {
        _timeProvider = timeProvider;
        _responder = responder;
    }

    public List<RecordedRequest> Requests { get; } = [];

    public IReadOnlyList<RecordedRequest> RequestsTo(string pathSuffix)
        => Requests.Where(r => r.Uri.AbsolutePath.EndsWith(pathSuffix, StringComparison.Ordinal)).ToList();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;

        // Buffered content, so reading it here cannot block on anything.
        var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();

        lock (Requests)
        {
            Requests.Add(new RecordedRequest(
                uri,
                _timeProvider.GetUtcNow(),
                new HttpRequestHeaders(
                    Header(request, "If-None-Match"),
                    Header(request, "If-Modified-Since"),
                    request.Headers.UserAgent.ToString() is { Length: > 0 } agent ? agent : null),
                body));
        }

        var ordinal = Requests.Count;

        return Task.FromResult(_responder(uri, ordinal));
    }

    private static string? Header(HttpRequestMessage request, string name)
        => request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

    public static HttpResponseMessage Text(HttpStatusCode status, string body, string? contentType = "text/html")
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body) };

        if (contentType is not null)
        {
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        }

        return response;
    }

    public static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound) { Content = new StringContent("") };
}

/// <summary>
/// Content that fails the test if anybody reads it. Used to prove that a 304
/// response body never leaves the wire.
/// </summary>
internal sealed class UnreadableContent : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => throw new InvalidOperationException("The body of a 304 response must never be downloaded.");

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
