using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Fetching;

namespace ProcurementRadar.Domain.Adapters;

/// <summary>
/// The conditional headers from the previous archive row for this source.
/// </summary>
public sealed record ConditionalRequest(string? ETag, string? LastModified)
{
    public static ConditionalRequest None { get; } = new(null, null);
}

/// <summary>
/// Everything an authority-specific integration has to provide.
/// </summary>
/// <remarks>
/// The two halves never meet. <see cref="FetchAsync"/> reaches the network and
/// returns a body it does not look inside. <see cref="Parse"/> is a pure
/// function over that body: no network, no database, no clock, no file system.
/// That separation is what makes it possible to fix a broken parser and
/// reprocess the archive instead of losing the data.
/// </remarks>
public interface ISourceAdapter
{
    /// <summary>Matches <c>source.adapter_key</c>.</summary>
    string Key { get; }

    Task<FetchResult> FetchAsync(
        Source source,
        ConditionalRequest conditional,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads notices out of a body. Never throws on malformed input: a bad row is
    /// skipped and logged, and the rest of the page still comes back.
    /// </summary>
    IReadOnlyList<RawNotice> Parse(string body, Source source);

    /// <summary>
    /// Asked only when <see cref="Parse"/> returned nothing: was the listing
    /// there and empty, or could the page not be read at all?
    /// </summary>
    /// <remarks>
    /// The health rule in spec.md Bölüm 11 - HTTP 200, zero notices, content
    /// hash moved - is the one that catches a redesigned site within hours
    /// instead of within a month. It cannot tell that apart from a municipality
    /// that simply has no open tender this week, and Kahramankazan proved the
    /// difference matters: it served
    /// <c>&lt;div class="common-list"&gt;&lt;ul&gt;&lt;/ul&gt;&lt;/div&gt;</c>
    /// for three days and raised a parser-broken alarm every one of them. The
    /// selectors were correct the whole time.
    ///
    /// A default of false keeps this honest for adapters that cannot tell: they
    /// go on alarming, which is the safe direction. Only an adapter that can
    /// actually see its container answers true.
    ///
    /// Separate from <see cref="Parse"/> rather than folded into its return
    /// type, because that signature is spec.md Bölüm 6 and is implemented four
    /// times and called from forty tests. A question asked only in the
    /// zero-notice case does not need to change what every caller receives.
    /// </remarks>
    bool ListWasEmpty(string body, Source source) => false;
}
