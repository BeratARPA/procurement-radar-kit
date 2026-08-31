using System.Net.Sockets;

namespace ProcurementRadar.Collector;

/// <summary>
/// Tells a name that will not resolve apart from a machine that is off the
/// network. Both arrive as the same exception; only the company they keep
/// separates them.
/// </summary>
/// <remarks>
/// Written for something the log had been saying for three weeks. Of 1769 fetch
/// failures over one stretch, 1672 were name resolution, and 1676 of those fell
/// in minutes where three or more unrelated hosts failed together - a health
/// directorate, a municipality, a university and a gas company do not go down in
/// the same second. This machine's network drops during the day, in twelve
/// windows totalling four hours, and none at all between midnight and six.
///
/// That distinction has to be drawn because both consequences of getting it
/// wrong are ours to pay:
///
///   * robots.txt kept the verdict for fifteen minutes, so a blip lasting
///     seconds cost a source its next poll. 112 polls were skipped while other
///     hosts were demonstrably answering.
///   * a failed fetch increments consecutive_failures, and ten deactivates the
///     source. The longest outage measured was 98 minutes, which is about six
///     consecutive failures for every source at once. A longer one would have
///     switched off all ninety.
///
/// The evidence is simultaneity, which is what a person reads off the log:
/// several distinct hosts failing to resolve with no successful contact in
/// between is this machine; one host failing while the others answer is that
/// host. A domain that genuinely stops resolving - it has happened here once -
/// still fails alone, still counts, and still reaches quarantine (D-22).
///
/// The first host of an outage is judged as the host's fault, because at that
/// moment there is nothing to compare it with. That costs one spurious failure
/// per outage rather than one per source per poll.
/// </remarks>
public sealed class NameResolutionWatch
{
    private readonly object _lock = new();
    private readonly HashSet<string> _unresolved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Any answer from any host, including an error status: the packets flowed,
    /// so whatever was wrong with this machine's network is over.
    /// </summary>
    public void Reached()
    {
        lock (_lock)
        {
            _unresolved.Clear();
        }
    }

    /// <summary>
    /// Records that <paramref name="host"/>'s name did not resolve and answers
    /// whether the fault looks like this machine's.
    /// </summary>
    /// <remarks>
    /// Recording the same host twice is deliberate and harmless - the fetcher
    /// and the robots guard both ask, about the same host, in the same poll.
    /// </remarks>
    public bool LooksLikeOurNetwork(string host)
    {
        lock (_lock)
        {
            _unresolved.Add(host);

            // Two is the smallest number that can be a coincidence and usually
            // is not. During the measured outages it was reached in seconds.
            return _unresolved.Count >= 2;
        }
    }

    /// <summary>
    /// Whether this failure is this machine being off the network rather than
    /// anything about <paramref name="host"/>.
    /// </summary>
    /// <remarks>
    /// The one place the question is answered, because it is asked from four:
    /// the fetcher, the robots guard, the telegram client and the callback
    /// loop. They are not four questions. The callback loop was still reporting
    /// 24 errors an outage about api.telegram.org being unreachable while the
    /// collector had already stopped blaming anybody for the same minutes, and
    /// D-07 counts those errors in the weekly report.
    /// </remarks>
    public bool IsOurOutage(Exception exception, string host)
        => IsNameResolutionFailure(exception) && LooksLikeOurNetwork(host);

    /// <summary>
    /// Whether this exception means the name never resolved, so no byte left
    /// this machine.
    /// </summary>
    /// <remarks>
    /// A connection that was refused or reset is not included: the name did
    /// resolve, which means dns worked and the far end is a real address that
    /// said no. That is the host's answer, not our absence.
    /// </remarks>
    public static bool IsNameResolutionFailure(Exception exception)
    {
        if (exception is HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError })
        {
            return true;
        }

        // Belt and braces for the paths that wrap the socket error themselves.
        for (var inner = exception; inner is not null; inner = inner.InnerException)
        {
            if (inner is SocketException socket
                && socket.SocketErrorCode is SocketError.HostNotFound
                    or SocketError.NoData
                    or SocketError.TryAgain)
            {
                return true;
            }
        }

        return false;
    }
}
