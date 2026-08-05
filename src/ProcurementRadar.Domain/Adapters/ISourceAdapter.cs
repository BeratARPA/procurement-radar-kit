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
}
