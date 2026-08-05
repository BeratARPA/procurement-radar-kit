using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ProcurementRadar.Domain.Normalization;

/// <summary>
/// Produces <c>procurement_notice.external_key</c>, the value that decides
/// whether a notice is one we already have (spec Bölüm 8).
/// </summary>
/// <remarks>
/// The same tender turns up on EKAP and on the authority's own site, worded
/// slightly differently each time. Getting this wrong in one direction floods
/// the notifications with duplicates; getting it wrong in the other hides a
/// notice behind one that only looked like it.
/// </remarks>
public sealed class ExternalKeyBuilder
{
    /// <summary>
    /// Notices are dated the way the page prints them, which is the Istanbul
    /// calendar day. Using the UTC day would split a deadline just after
    /// midnight local time away from the same deadline written without a time.
    /// </summary>
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private const string NoDeadline = "no-deadline";

    private readonly NoticeNormalizer _normalizer;

    public ExternalKeyBuilder(NoticeNormalizer normalizer)
    {
        _normalizer = normalizer;
    }

    public string Build(
        string? ikn,
        string authorityName,
        string title,
        DateTimeOffset? bidDeadlineAt)
    {
        // An IKN is the state's own identifier for the tender. Nothing we could
        // compute is a better answer than the one everybody already agrees on.
        if (!string.IsNullOrWhiteSpace(ikn))
        {
            return NormalizeIkn(ikn);
        }

        var day = bidDeadlineAt is { } deadline
            ? TimeZoneInfo.ConvertTime(deadline, Istanbul).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : NoDeadline;

        var material = string.Create(
            CultureInfo.InvariantCulture,
            $"{_normalizer.Fold(authorityName)}|{_normalizer.Normalize(title)}|{day}");

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    /// <summary>
    /// Whitespace only. An IKN is a state-issued code and its digits and slash
    /// are not ours to fold away.
    /// </summary>
    private static string NormalizeIkn(string ikn)
        => string.Join(' ', ikn.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
